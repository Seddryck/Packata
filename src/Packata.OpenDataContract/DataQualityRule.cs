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

    [Label("Metric")]
    public string? Metric { get; set; }

    [Obsolete("Use Metric instead.")]
    public string? Rule { get; set; }

    [Label("Arguments")]
    public Dictionary<string, object> Arguments { get; set; } = [];

    [Label("Unit")]
    public string? Unit { get; set; }

    public object? MustBe { get; set; }
    public object? MustNotBe { get; set; }
    public object? MustBeGreaterThan { get; set; }
    public object? MustBeGreaterOrEqualTo { get; set; }
    public object? MustBeLessThan { get; set; }
    public object? MustBeLessOrEqualTo { get; set; }
    public List<object> MustBeBetween { get; set; } = [];
    public List<object> MustNotBeBetween { get; set; } = [];

    [Label("SQL Query")]
    public string? Query { get; set; }

    [Label("Engine")]
    public string? Engine { get; set; }

    [Label("Implementation")]
    public string? Implementation { get; set; }

    [Label("Scheduler")]
    public string? Scheduler { get; set; }

    [Label("Schedule")]
    public string? Schedule { get; set; }

    [Label("Authoritative Definitions")]
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];

    [Label("Tags")]
    public List<string> Tags { get; set; } = [];

    [Label("Custom Properties")]
    public CustomProperties CustomProperties { get; set; } = [];
}
