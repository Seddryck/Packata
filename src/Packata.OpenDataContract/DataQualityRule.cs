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

    private readonly Dictionary<string, object?> _expectations = [];

    [YamlDotNet.Serialization.YamlIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string, object?> DeclaredExpectations => _expectations;

    public object? MustBe { get => Get(nameof(MustBe)); set => _expectations[nameof(MustBe)] = value; }
    public object? MustNotBe { get => Get(nameof(MustNotBe)); set => _expectations[nameof(MustNotBe)] = value; }
    public object? MustBeGreaterThan { get => Get(nameof(MustBeGreaterThan)); set => _expectations[nameof(MustBeGreaterThan)] = value; }
    public object? MustBeGreaterOrEqualTo { get => Get(nameof(MustBeGreaterOrEqualTo)); set => _expectations[nameof(MustBeGreaterOrEqualTo)] = value; }
    public object? MustBeLessThan { get => Get(nameof(MustBeLessThan)); set => _expectations[nameof(MustBeLessThan)] = value; }
    public object? MustBeLessOrEqualTo { get => Get(nameof(MustBeLessOrEqualTo)); set => _expectations[nameof(MustBeLessOrEqualTo)] = value; }
    private List<object> _MustBeBetween = [];
    public List<object> MustBeBetween { get => _MustBeBetween; set { _MustBeBetween = value; _expectations[nameof(MustBeBetween)] = value; } }
    private List<object> _MustNotBeBetween = [];
    public List<object> MustNotBeBetween { get => _MustNotBeBetween; set { _MustNotBeBetween = value; _expectations[nameof(MustNotBeBetween)] = value; } }

    private object? Get(string name) => _expectations.GetValueOrDefault(name);

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
