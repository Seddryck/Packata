using Packata.Core;

namespace Packata.OpenDataContract;

public class SupportChannel
{
    [Label("ID")]
    public string? Id { get; set; }

    [Label("Channel")]
    public required string Channel { get; set; }

    [Label("Description")]
    public string? Description { get; set; }

    [Label("Invitation URL")]
    public string? InvitationUrl { get; set; }

    [Label("Scope")]
    public string? Scope { get; set; }

    [Label("Tool")]
    public string? Tool { get; set; }

    [Label("URL")]
    public string? Url { get; set; }

    [Label("Custom Properties")]
    public CustomProperties CustomProperties { get; set; } = [];
}
