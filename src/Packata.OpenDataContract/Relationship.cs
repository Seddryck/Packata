using Packata.Core;

namespace Packata.OpenDataContract;

public class Relationship
{
    [Label("ID")]
    public string? Id { get; set; }

    [Label("Type")]
    public string Type { get; set; } = "foreignKey";

    [Label("From")]
    public object? From { get; set; }

    [Label("To")]
    public required object To { get; set; }

    [Label("Custom Properties")]
    public CustomProperties CustomProperties { get; set; } = [];
}
