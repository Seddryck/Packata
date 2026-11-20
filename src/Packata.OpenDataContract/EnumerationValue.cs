using YamlDotNet.Serialization;

namespace Packata.OpenDataContract;

public class EnumerationValue
{
    public string? Id { get; set; }
    public required object Value { get; set; }
    public string? Label { get; set; }
    public string? Description { get; set; }
    [YamlIgnore]
    public List<string> Tags { get; set; } = [];

    /// <summary>Gets or sets the ordered string and key/value tags.</summary>
    /// <value>Plain labels and key/value entries in declaration order.</value>
    [YamlMember(Alias = "tags", ApplyNamingConventions = false)]
    [System.Text.Json.Serialization.JsonIgnore]
    public TagEntries TagEntries
    {
        get => tagEntries.WithLabels(Tags);
        set
        {
            tagEntries = value;
            Tags = value.Labels.ToList();
        }
    }

    private TagEntries tagEntries = new([]);
    public CustomProperties CustomProperties { get; set; } = [];
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];
}
