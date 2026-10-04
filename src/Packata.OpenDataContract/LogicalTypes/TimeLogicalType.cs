namespace Packata.OpenDataContract.Types;

/// <summary>
/// Additional metadata options for the ODCS logical type "time".
/// </summary>
public class TimeLogicalType : BaseLogicalType<TimeOnly>
{
    public TimeLogicalType(Dictionary<string, object>? options)
        : base(options)
    {
        Timezone = ConvertBoolean(options?.GetValueOrDefault("timezone"));
        DefaultTimezone = options?.GetValueOrDefault("defaultTimezone") as string ?? "Etc/UTC";
    }

    public bool? Timezone { get; set; }

    public string DefaultTimezone { get; set; }

    private static bool? ConvertBoolean(object? value)
        => value switch
        {
            bool boolean => boolean,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => null
        };

    protected override TimeOnly? ConvertValue(object? value)
        => value is string text && TimeOnly.TryParse(text, out var result) ? result : null;
}
