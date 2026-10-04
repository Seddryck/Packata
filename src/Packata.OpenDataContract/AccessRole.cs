using Packata.Core;

namespace Packata.OpenDataContract;

public class AccessRole
{
    [Label("ID")]
    public string? Id { get; set; }

    [Label("Role")]
    public required string Role { get; set; }

    [Label("Access")]
    public string? Access { get; set; }

    [Label("Description")]
    public string? Description { get; set; }

    [Label("First-level Approvers")]
    public string? FirstLevelApprovers { get; set; }

    [Label("Second-level Approvers")]
    public string? SecondLevelApprovers { get; set; }

    [Label("Custom Properties")]
    public CustomProperties CustomProperties { get; set; } = [];
}
