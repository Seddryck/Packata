using Packata.Core;
using YamlDotNet.Serialization;

namespace Packata.OpenDataContract;

public class Team
{
    [Label("ID")]
    public string? Id { get; set; }

    [Label("Name")]
    public string? Name { get; set; }

    [Label("Description")]
    public string? Description { get; set; }

    [Label("Members")]
    public List<TeamMember> Members { get; set; } = [];

    [Label("Authoritative Definitions")]
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];

    [Label("Tags")]
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

    [Label("Custom Properties")]
    public CustomProperties CustomProperties { get; set; } = [];

    [YamlIgnore]
    public bool UsesDeprecatedArrayStructure { get; internal set; }
}

public class TeamMember
{
    public string? Id { get; set; }
    public string? Username { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Role { get; set; }
    public DateOnly? DateIn { get; set; }
    public DateOnly? DateOut { get; set; }
    public string? ReplacedByUsername { get; set; }
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];
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
}

internal class TeamDocument
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<TeamMember> Members { get; set; } = [];
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];
    public TagEntries Tags { get; set; } = new([]);
    public CustomProperties CustomProperties { get; set; } = [];
}
