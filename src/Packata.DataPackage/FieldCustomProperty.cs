namespace Packata.DataPackage;

/// <summary>An entry in a Data Package field's Packata custom properties.</summary>
public sealed class FieldCustomProperty
{
    public string? Property { get; set; }
    public object? Value { get; set; }
}
