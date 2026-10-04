using Packata.Core;

namespace Packata.OpenDataContract;

public class ServiceLevelAgreementProperty
{
    [Label("ID")]
    public string? Id { get; set; }

    [Label("Property")]
    public required string Property { get; set; }

    [Label("Value")]
    public required object Value { get; set; }

    [Label("Extended Value")]
    public object? ValueExt { get; set; }

    [Label("Unit")]
    public string? Unit { get; set; }

    [Label("Element")]
    public string? Element { get; set; }

    [Label("Driver")]
    public string? Driver { get; set; }

    [Label("Scheduler")]
    public string? Scheduler { get; set; }

    [Label("Schedule")]
    public string? Schedule { get; set; }

    [Label("Authoritative Definitions")]
    public List<AuthoritativeDefinition> AuthoritativeDefinitions { get; set; } = [];

    [Label("Custom Properties")]
    public CustomProperties CustomProperties { get; set; } = [];
}
