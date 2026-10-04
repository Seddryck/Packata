namespace Packata.OpenDataContract.Types;

/// <summary>
/// Additional metadata options for the ODCS logical type "timestamp".
/// </summary>
public class TimestampLogicalType : BaseLogicalType<DateTimeOffset>
{
    public TimestampLogicalType(Dictionary<string, object>? options)
        : base(options)
    { }

    protected override DateTimeOffset? ConvertValue(object? value)
        => value is string text && DateTimeOffset.TryParse(text, out var result) ? result : null;
}
