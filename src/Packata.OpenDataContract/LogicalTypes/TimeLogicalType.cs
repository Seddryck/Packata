namespace Packata.OpenDataContract.Types;

/// <summary>
/// Additional metadata options for the ODCS logical type "time".
/// </summary>
public class TimeLogicalType : BaseLogicalType<TimeOnly>
{
    public TimeLogicalType(Dictionary<string, object>? options)
        : base(options)
    { }

    protected override TimeOnly? ConvertValue(object? value)
        => value is string text && TimeOnly.TryParse(text, out var result) ? result : null;
}
