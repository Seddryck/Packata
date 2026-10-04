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
    public List<string> Tags { get; set; } = [];

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
    public List<string> Tags { get; set; } = [];
    public CustomProperties CustomProperties { get; set; } = [];
}

internal class TeamDocument
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<TeamMember> Members { get; set; } = [];
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public CustomProperties CustomProperties { get; set; } = [];
}
