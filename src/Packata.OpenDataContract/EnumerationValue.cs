namespace Packata.OpenDataContract;

public class EnumerationValue() : TaggedElement<List<string>>(labels => labels.ToList())
{
    public string? Id { get; set; }
    public required object Value { get; set; }
    public string? Label { get; set; }
    public string? Description { get; set; }
    public CustomProperties CustomProperties { get; set; } = [];
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];
}
