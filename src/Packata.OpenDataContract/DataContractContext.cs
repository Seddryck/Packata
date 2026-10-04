using Packata.Core;
using YamlDotNet.Serialization;

namespace Packata.OpenDataContract;

public class DataContractContext
{
    [Label("Instructions")]
    public string? Instructions { get; set; }

    [Label("Verified Statements")]
    public List<VerifiedStatement> VerifiedStatements { get; set; } = [];

    [Label("Constraints")]
    public List<ContextConstraint> Constraints { get; set; } = [];

    [YamlIgnore]
    public bool UsesStringShorthand { get; internal set; }
}

public class VerifiedStatement
{
    public string? Id { get; set; }
    public required string Question { get; set; }
    public string? Answer { get; set; }
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public CustomProperties CustomProperties { get; set; } = [];
}

public class ContextConstraint
{
    public string? Id { get; set; }
    public required string Constraint { get; set; }
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public CustomProperties CustomProperties { get; set; } = [];
}

internal class ContextDocument
{
    public string? Instructions { get; set; }
    public List<VerifiedStatement> VerifiedStatements { get; set; } = [];
    public List<ContextConstraint> Constraints { get; set; } = [];
}
