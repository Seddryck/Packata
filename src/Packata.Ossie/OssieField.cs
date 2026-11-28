using System.Text.Json.Serialization;

namespace Packata.Ossie;

public sealed record OssieField
{
    public required string Name { get; init; }

    public required OssieExpression Expression { get; init; }

    public OssieDimension? Dimension { get; init; }

    public string? Label { get; init; }

    public string? Description { get; init; }

    [JsonPropertyName("datatype")]
    public OssieDataType? DataType { get; init; }

    public OssieAiContext? AiContext { get; init; }

    public IReadOnlyList<OssieExtension>? NonportableExtensions { get; init; }
}
