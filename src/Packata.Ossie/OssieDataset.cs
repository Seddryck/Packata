namespace Packata.Ossie;

public sealed record OssieDataset
{
    public required string Name { get; init; }

    public required string Source { get; init; }

    public IReadOnlyList<string>? PrimaryKey { get; init; }

    public IReadOnlyList<IReadOnlyList<string>>? UniqueKeys { get; init; }

    public string? Description { get; init; }

    public OssieAiContext? AiContext { get; init; }

    public IReadOnlyList<OssieField>? Fields { get; init; }

    public IReadOnlyList<OssieMetric>? Metrics { get; init; }

    public IReadOnlyList<OssieExtension>? NonportableExtensions { get; init; }
}
