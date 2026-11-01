using System.Globalization;
using System.Text.RegularExpressions;
using Packata.Core.Contracts;

namespace Packata.OpenDataContract.Mapping;

internal static class ServiceCommitmentMapper
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private const RegexOptions MatchOptions = RegexOptions.IgnoreCase | RegexOptions.NonBacktracking;
    private const string UnsupportedSchemaPath = "Unsupported schema element path.";

    private enum CommitmentKind
    {
        Unknown,
        Freshness,
        Latency,
        Frequency,
        Retention,
        Availability,
        TimeOfAvailability,
        GeneralAvailability,
        EndOfSupport,
        EndOfLife
    }

    private static readonly Dictionary<string, CommitmentKind> PropertyKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["freshness"] = CommitmentKind.Freshness,
        ["latency"] = CommitmentKind.Latency, ["ly"] = CommitmentKind.Latency,
        ["frequency"] = CommitmentKind.Frequency, ["fy"] = CommitmentKind.Frequency,
        ["retention"] = CommitmentKind.Retention, ["re"] = CommitmentKind.Retention,
        ["availability"] = CommitmentKind.Availability, ["av"] = CommitmentKind.Availability,
        ["timeOfAvailability"] = CommitmentKind.TimeOfAvailability,
        ["generalAvailability"] = CommitmentKind.GeneralAvailability, ["ga"] = CommitmentKind.GeneralAvailability,
        ["endOfSupport"] = CommitmentKind.EndOfSupport, ["es"] = CommitmentKind.EndOfSupport,
        ["endOfLife"] = CommitmentKind.EndOfLife, ["el"] = CommitmentKind.EndOfLife
    };

    internal sealed record Result(Sla? Sla, Lifecycle? Lifecycle,
        IReadOnlyList<ServiceLevelAgreementProperty> Unmapped);

    internal static Result Map(DataContract document, ICollection<MappingDiagnostic> diagnostics)
    {
        var commitments = new Commitments();
        var unmapped = new List<ServiceLevelAgreementProperty>();
        for (var index = 0; index < document.SlaProperties.Count; index++)
        {
            var source = document.SlaProperties[index];
            try
            {
                if (commitments.Add(source, document))
                    continue;
                unmapped.Add(source);
                diagnostics.Add(new("ODCS004", MappingSeverity.Warning, $"slaProperties/{index}",
                    "Unsupported SLA property retained in ODCS extension metadata."));
            }
            catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException or RegexMatchTimeoutException)
            {
                unmapped.Add(source);
                diagnostics.Add(new("ODCS005", MappingSeverity.Warning, $"slaProperties/{index}",
                    $"SLA commitment could not be normalized and was retained in ODCS extension metadata. {exception.Message}"));
            }
        }
        return commitments.Build(unmapped);
    }

    private sealed class Commitments
    {
        private readonly Dictionary<CommitmentKind, List<DurationCommitment>> _durations = [];
        private readonly Dictionary<CommitmentKind, DateTimeOffset> _milestones = [];
        private readonly List<AvailabilityTarget> _targets = [];
        private readonly List<AvailabilityWindow> _windows = [];

        public bool Add(ServiceLevelAgreementProperty source, DataContract document)
        {
            var kind = PropertyKinds.GetValueOrDefault(source.Property);
            var elements = ParseElements(ElementPath(source, document, kind), document);
            switch (kind)
            {
                case CommitmentKind.Freshness or CommitmentKind.Latency or CommitmentKind.Frequency or CommitmentKind.Retention:
                    AddDuration(source, kind, elements);
                    break;
                case CommitmentKind.GeneralAvailability or CommitmentKind.EndOfSupport or CommitmentKind.EndOfLife:
                    AddMilestone(source, kind, elements);
                    break;
                case CommitmentKind.Availability:
                    _targets.Add(ParseTarget(source, elements));
                    break;
                case CommitmentKind.TimeOfAvailability:
                    _windows.Add(ParseWindow(source, elements));
                    break;
                default:
                    return false;
            }
            return true;
        }

        private void AddDuration(ServiceLevelAgreementProperty source, CommitmentKind kind, IReadOnlyList<ElementReference> elements)
        {
            if (source.ValueExt is not null)
                throw new FormatException("Extended duration bounds are not supported.");
            var duration = ParseDuration(source.Value, source.Unit);
            if (!_durations.TryGetValue(kind, out var list))
                _durations[kind] = list = [];
            list.Add(new(duration, elements, source.Description));
        }

        private void AddMilestone(ServiceLevelAgreementProperty source, CommitmentKind kind, IReadOnlyList<ElementReference> elements)
        {
            if (elements.Count > 0 || source.ValueExt is not null)
                throw new FormatException("Scoped or extended lifecycle milestones are not supported.");
            var timestamp = ParseTimestamp(source.Value);
            if (!_milestones.TryAdd(kind, timestamp))
                throw new FormatException("Duplicate lifecycle milestone; the first value was retained.");
        }

        public Result Build(IReadOnlyList<ServiceLevelAgreementProperty> unmapped)
        {
            var availability = _targets.Count + _windows.Count == 0 ? null : new Availability(_targets, _windows);
            var sla = _durations.Count == 0 && availability is null ? null : new Sla(
                _durations.GetValueOrDefault(CommitmentKind.Freshness), _durations.GetValueOrDefault(CommitmentKind.Latency),
                _durations.GetValueOrDefault(CommitmentKind.Frequency), _durations.GetValueOrDefault(CommitmentKind.Retention), availability);
            var lifecycle = _milestones.Count == 0 ? null : new Lifecycle(
                Milestone(CommitmentKind.GeneralAvailability), Milestone(CommitmentKind.EndOfSupport), Milestone(CommitmentKind.EndOfLife));
            return new(sla, lifecycle, unmapped);
        }

        private DateTimeOffset? Milestone(CommitmentKind kind) => _milestones.TryGetValue(kind, out var value) ? value : null;
    }

    private static string? ElementPath(ServiceLevelAgreementProperty source, DataContract document, CommitmentKind kind)
    {
        if (kind is CommitmentKind.GeneralAvailability or CommitmentKind.EndOfSupport or CommitmentKind.EndOfLife)
            return source.Element;
        // A deprecated explicit default is still a source commitment, not an inferred one.
#pragma warning disable CS0618
        return source.Element ?? document.SlaDefaultElement;
#pragma warning restore CS0618
    }

    private static AvailabilityTarget ParseTarget(ServiceLevelAgreementProperty source, IReadOnlyList<ElementReference> elements)
    {
        if (source.ValueExt is not null || source.Unit is not (null or "%" or "percent"))
            throw new FormatException("Availability must be a percentage.");
        var percentage = decimal.Parse(Text(source.Value), NumberStyles.Float, Invariant);
        if (percentage is < 0 or > 100)
            throw new FormatException("Availability must be between 0 and 100 percent.");
        return new(percentage, elements, source.Description);
    }

    private static AvailabilityWindow ParseWindow(ServiceLevelAgreementProperty source, IReadOnlyList<ElementReference> elements)
    {
        var start = ParseTime(source.Value);
        var end = source.ValueExt is null ? ((TimeOnly Time, TimeSpan? Offset)?)null : ParseTime(source.ValueExt);
        if (end is not null && start.Offset != end.Value.Offset)
            throw new FormatException("Availability bounds must have compatible time zones.");
        return new(start.Time, end?.Time, start.Offset, elements, source.Description);
    }

    private static string Text(object value) => Convert.ToString(value, Invariant) ?? "";

    private static ContractDuration ParseDuration(object value, string? unit)
    {
        var number = decimal.Parse(Text(value), NumberStyles.Float, Invariant);
        if (number < 0)
            throw new FormatException("Durations cannot be negative.");
        var normalized = unit?.Trim().ToLowerInvariant();
        if (normalized is "y" or "yr" or "yrs" or "year" or "years")
            return CalendarDuration(number * 12);
        if (normalized is "mo" or "month" or "months")
            return CalendarDuration(number);
        var total = number * TicksPerUnit(normalized);
        if (total != decimal.Truncate(total))
            throw new FormatException("Duration precision exceeds one tick.");
        return new(0, TimeSpan.FromTicks(checked((long)total)));
    }

    private static ContractDuration CalendarDuration(decimal months)
    {
        if (months != decimal.Truncate(months))
            throw new FormatException("Calendar durations must resolve to whole months.");
        return new(checked((int)months), TimeSpan.Zero);
    }

    private static decimal TicksPerUnit(string? unit) => unit switch
    {
        "w" or "week" or "weeks" => 7m * TimeSpan.TicksPerDay,
        "d" or "day" or "days" => TimeSpan.TicksPerDay,
        "h" or "hr" or "hrs" or "hour" or "hours" => TimeSpan.TicksPerHour,
        "m" or "min" or "mins" or "minute" or "minutes" => TimeSpan.TicksPerMinute,
        "s" or "sec" or "secs" or "second" or "seconds" => TimeSpan.TicksPerSecond,
        "ms" or "millisecond" or "milliseconds" => TimeSpan.TicksPerMillisecond,
        _ => throw new FormatException("Missing or unsupported duration unit.")
    };

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
        if (!Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$", MatchOptions, MatchTimeout))
            throw new FormatException("Timestamps must use ISO 8601 with an explicit time zone.");
        return DateTimeOffset.Parse(text, Invariant).ToUniversalTime();
    }

    private static (TimeOnly Time, TimeSpan? Offset) ParseTime(object value)
    {
        var text = Text(value);
        if (!Regex.IsMatch(text, @"^\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})?$", MatchOptions, MatchTimeout))
            throw new FormatException("Availability times must use ISO 8601 time notation.");
        if (Regex.IsMatch(text, @"(Z|[+-]\d{2}:\d{2})$", MatchOptions, MatchTimeout))
        {
            var timestamp = DateTimeOffset.Parse("2000-01-01T" + text, Invariant);
            return (TimeOnly.FromDateTime(timestamp.UtcDateTime), TimeSpan.Zero);
        }
        return (TimeOnly.Parse(text, Invariant), null);
    }

    private static IReadOnlyList<ElementReference> ParseElements(string? text, DataContract document)
        => string.IsNullOrWhiteSpace(text) ? [] : text.Split(',').Select(item => ParseElement(item.Trim(), document)).ToArray();

    private static ElementReference ParseElement(string text, DataContract document)
    {
        if (text.Contains('/'))
            return ParseSchemaElement(text);
        var parts = text.Split('.');
        if (parts.Length >= 2 && parts.All(part => !string.IsNullOrWhiteSpace(part)))
            return new ElementReference(parts[0], string.Join('.', parts.Skip(1)));
        if (parts.Length == 1 && parts[0].Length > 0)
            return ParseUnqualifiedElement(parts[0], document);
        throw new FormatException("Element references require a dataset and optional field; unqualified fields require one dataset.");
    }

    private static ElementReference ParseUnqualifiedElement(string name, DataContract document)
    {
        if (document.Schema.Any(schema => schema.Name == name))
            return new ElementReference(name);
        if (document.Schema.Count == 1)
            return new ElementReference(document.Schema[0].Name, name);
        throw new FormatException("Element references require a dataset and optional field; unqualified fields require one dataset.");
    }

    private static ElementReference ParseSchemaElement(string text)
    {
        var path = text.Split('/');
        if (path.Length < 2 || path[0] != "schema" || string.IsNullOrWhiteSpace(path[1]))
            throw new FormatException("Unsupported element path.");
        if (path.Length == 2)
            return new ElementReference(path[1]);
        ValidateFieldPath(path);
        var fields = Enumerable.Range(1, (path.Length - 2) / 2).Select(index => path[(index * 2) + 1]);
        return new ElementReference(path[1], string.Join('.', fields));
    }

    private static void ValidateFieldPath(string[] path)
    {
        if (path.Length < 4 || path.Length % 2 != 0)
            throw new FormatException(UnsupportedSchemaPath);
        for (var index = 2; index < path.Length; index += 2)
        {
            if (path[index] != "properties" || string.IsNullOrWhiteSpace(path[index + 1]))
                throw new FormatException(UnsupportedSchemaPath);
        }
    }
}
