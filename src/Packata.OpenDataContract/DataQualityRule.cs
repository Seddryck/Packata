using Packata.Core;

namespace Packata.OpenDataContract;

public class DataQualityRule
{
    [Label("ID")]
    public string? Id { get; set; }

    [Label("Type")]
    public string? Type { get; set; }

    [Label("Name")]
    public string? Name { get; set; }

    [Label("Description")]
    public string? Description { get; set; }

    [Label("Business Impact")]
    public string? BusinessImpact { get; set; }

    [Label("Dimension")]
    public string? Dimension { get; set; }

    [Label("Method")]
    public string? Method { get; set; }

    [Label("Severity")]
    public string? Severity { get; set; }

    [Label("Authoritative Definitions")]
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];

    [Label("Tags")]
    public List<string> Tags { get; set; } = [];

    [Label("Custom Properties")]
    public CustomProperties CustomProperties { get; set; } = [];
}
