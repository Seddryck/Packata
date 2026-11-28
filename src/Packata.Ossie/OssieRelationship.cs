namespace Packata.Ossie;

public sealed record OssieRelationship
{
    public required string Name { get; init; }

    public required string From { get; init; }

    public required string To { get; init; }

    public required IReadOnlyList<string> FromColumns { get; init; }

    public required IReadOnlyList<string> ToColumns { get; init; }

    public OssieAiContext? AiContext { get; init; }

    public IReadOnlyList<OssieExtension>? NonportableExtensions { get; init; }
}
