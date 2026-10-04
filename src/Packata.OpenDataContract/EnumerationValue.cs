namespace Packata.OpenDataContract;

public class EnumerationValue
{
    public string? Id { get; set; }
    public required object Value { get; set; }
    public string? Label { get; set; }
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = [];
    public CustomProperties CustomProperties { get; set; } = [];
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];
}
