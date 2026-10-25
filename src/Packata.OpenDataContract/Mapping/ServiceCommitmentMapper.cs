using System.Globalization;
using System.Text.RegularExpressions;
using Packata.Core.Contracts;

namespace Packata.OpenDataContract.Mapping;

internal static class ServiceCommitmentMapper
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    internal sealed record Result(Sla? Sla, Lifecycle? Lifecycle,
        IReadOnlyList<ServiceLevelAgreementProperty> Unmapped);

    internal static Result Map(DataContract document, ICollection<MappingDiagnostic> diagnostics)
    {
        var durations = new Dictionary<string, List<DurationCommitment>>();
        var milestones = new Dictionary<string, DateTimeOffset>();
        var targets = new List<AvailabilityTarget>();
        var windows = new List<AvailabilityWindow>();
        var unmapped = new List<ServiceLevelAgreementProperty>();
        for (var index = 0; index < document.SlaProperties.Count; index++)
        {
            var source = document.SlaProperties[index];
            var property = source.Property.ToLowerInvariant() switch
            {
                "ly" => "latency", "fy" => "frequency", "re" => "retention",
                "av" => "availability", "ga" => "generalavailability",
                "es" => "endofsupport", "el" => "endoflife",
                var name => name
            };
            try
            {
                // A deprecated explicit default is still a source commitment, not an inferred one.
#pragma warning disable CS0618
                var elements = ParseElements(source.Element ?? (property is "generalavailability" or "endofsupport" or "endoflife"
                    ? null : document.SlaDefaultElement), document);
#pragma warning restore CS0618
                switch (property)
                {
                    case "freshness" or "latency" or "frequency" or "retention":
                        if (source.ValueExt is not null)
                            throw new FormatException("Extended duration bounds are not supported.");
                        var duration = ParseDuration(source.Value, source.Unit);
                        if (!durations.TryGetValue(property, out var list))
                            durations[property] = list = [];
                        list.Add(new(duration, elements, source.Description));
                        break;
                    case "generalavailability" or "endofsupport" or "endoflife":
                        if (elements.Count > 0 || source.ValueExt is not null)
                            throw new FormatException("Scoped or extended lifecycle milestones are not supported.");
                        var timestamp = ParseTimestamp(source.Value);
                        if (!milestones.TryAdd(property, timestamp))
                            throw new FormatException("Duplicate lifecycle milestone; the first value was retained.");
                        break;
                    case "availability":
                        if (source.ValueExt is not null || source.Unit is not (null or "%" or "percent"))
                            throw new FormatException("Availability must be a percentage.");
                        var percentage = decimal.Parse(Text(source.Value), NumberStyles.Float, Invariant);
                        if (percentage is < 0 or > 100)
                            throw new FormatException("Availability must be between 0 and 100 percent.");
                        targets.Add(new(percentage, elements, source.Description));
                        break;
                    case "timeofavailability":
                        var start = ParseTime(source.Value);
                        var end = source.ValueExt is null ? ((TimeOnly Time, TimeSpan? Offset)?)null : ParseTime(source.ValueExt);
                        if (end is not null && start.Offset != end.Value.Offset)
                            throw new FormatException("Availability bounds must have compatible time zones.");
                        windows.Add(new(start.Time, end?.Time, start.Offset, elements, source.Description));
                        break;
                    default:
                        unmapped.Add(source);
                        diagnostics.Add(new("ODCS004", MappingSeverity.Warning, $"slaProperties/{index}",
                            "Unsupported SLA property retained in ODCS extension metadata."));
                        break;
                }
            }
            catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
            {
                unmapped.Add(source);
                diagnostics.Add(new("ODCS005", MappingSeverity.Warning, $"slaProperties/{index}",
                    $"SLA commitment could not be normalized and was retained in ODCS extension metadata. {exception.Message}"));
            }
        }

        var availability = targets.Count + windows.Count == 0 ? null : new Availability(targets, windows);
        var sla = durations.Count == 0 && availability is null ? null : new Sla(
            durations.GetValueOrDefault("freshness"), durations.GetValueOrDefault("latency"),
            durations.GetValueOrDefault("frequency"), durations.GetValueOrDefault("retention"), availability);
        return new(sla, milestones.Count == 0 ? null : new Lifecycle(
            Milestone("generalavailability"), Milestone("endofsupport"), Milestone("endoflife")), unmapped);

        DateTimeOffset? Milestone(string name) => milestones.TryGetValue(name, out var value) ? value : null;
    }

    private static string Text(object value) => Convert.ToString(value, Invariant) ?? "";

    private static ContractDuration ParseDuration(object value, string? unit)
    {
        var number = decimal.Parse(Text(value), NumberStyles.Float, Invariant);
        if (number < 0)
            throw new FormatException("Durations cannot be negative.");
        var normalized = unit?.Trim().ToLowerInvariant();
        if (normalized is "y" or "yr" or "yrs" or "year" or "years" or "mo" or "month" or "months")
        {
            var months = normalized is "mo" or "month" or "months" ? number : number * 12;
            if (months != decimal.Truncate(months))
                throw new FormatException("Calendar durations must resolve to whole months.");
            return new(checked((int)months), TimeSpan.Zero);
        }
        var ticks = normalized switch
        {
            "w" or "week" or "weeks" => 7m * TimeSpan.TicksPerDay,
            "d" or "day" or "days" => TimeSpan.TicksPerDay,
            "h" or "hr" or "hrs" or "hour" or "hours" => TimeSpan.TicksPerHour,
            "m" or "min" or "mins" or "minute" or "minutes" => TimeSpan.TicksPerMinute,
            "s" or "sec" or "secs" or "second" or "seconds" => TimeSpan.TicksPerSecond,
            "ms" or "millisecond" or "milliseconds" => TimeSpan.TicksPerMillisecond,
            _ => throw new FormatException("Missing or unsupported duration unit.")
        };
        var total = number * ticks;
        if (total != decimal.Truncate(total))
            throw new FormatException("Duration precision exceeds one tick.");
        return new(0, TimeSpan.FromTicks(checked((long)total)));
    }

    private static DateTimeOffset ParseTimestamp(object value)
    {
        if (value is DateOnly dateOnly)
            return new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (value is DateTimeOffset offset)
            return offset.ToUniversalTime();
        if (value is DateTime date)
        {
            if (date.Kind != DateTimeKind.Utc)
                throw new FormatException("Timestamps must include an explicit time zone.");
            return new DateTimeOffset(date);
        }
        var text = Text(value);
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var day))
            return new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (!Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase))
            throw new FormatException("Timestamps must use ISO 8601 with an explicit time zone.");
        return DateTimeOffset.Parse(text, Invariant).ToUniversalTime();
    }

    private static (TimeOnly Time, TimeSpan? Offset) ParseTime(object value)
    {
        var text = Text(value);
        if (!Regex.IsMatch(text, @"^\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})?$", RegexOptions.IgnoreCase))
            throw new FormatException("Availability times must use ISO 8601 time notation.");
        if (Regex.IsMatch(text, @"(Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase))
        {
            var timestamp = DateTimeOffset.Parse("2000-01-01T" + text, Invariant);
            return (TimeOnly.FromDateTime(timestamp.UtcDateTime), TimeSpan.Zero);
        }
        return (TimeOnly.Parse(text, Invariant), null);
    }

    private static IReadOnlyList<ElementReference> ParseElements(string? text, DataContract document)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        return text.Split(',').Select(item =>
        {
            var path = item.Trim().Split('/');
            if (path.Length >= 2 && path[0] == "schema" && !string.IsNullOrWhiteSpace(path[1]))
            {
                if (path.Length == 2)
                    return new ElementReference(path[1]);
                if (path.Length >= 4 && path.Length % 2 == 0 &&
                    Enumerable.Range(1, (path.Length - 2) / 2).All(index =>
                        path[index * 2] == "properties" && !string.IsNullOrWhiteSpace(path[index * 2 + 1])))
                    return new ElementReference(path[1], string.Join('.', path.Where((_, index) => index >= 3 && index % 2 == 1)));
                throw new FormatException("Unsupported schema element path.");
            }
            if (item.Contains('/'))
                throw new FormatException("Unsupported element path.");
            var parts = item.Trim().Split('.');
            if (parts.Length >= 2 && parts.All(part => !string.IsNullOrWhiteSpace(part)))
                return new ElementReference(parts[0], string.Join('.', parts.Skip(1)));
            if (parts.Length == 1 && parts[0].Length > 0)
            {
                if (document.Schema.Any(schema => schema.Name == parts[0]))
                    return new ElementReference(parts[0]);
                if (document.Schema.Count == 1)
                    return new ElementReference(document.Schema[0].Name, parts[0]);
            }
            throw new FormatException("Element references require a dataset and optional field; unqualified fields require one dataset.");
        }).ToArray();
    }
}
