namespace Packata.Ossie;

public sealed record OssieModel
{
    public string Version { get; init; } = OssieModelSerializer.SupportedVersion;

    public required string Name { get; init; }

    public string? Description { get; init; }

    public OssieAiContext? AiContext { get; init; }

    public required IReadOnlyList<OssieDataset> Datasets { get; init; }

    public IReadOnlyList<OssieRelationship>? Relationships { get; init; }

    public IReadOnlyList<OssieMetric>? Metrics { get; init; }

    public IReadOnlyList<OssieExtension>? NonportableExtensions { get; init; }
}
