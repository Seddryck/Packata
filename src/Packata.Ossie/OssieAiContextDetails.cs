using System.Text.Json;
using System.Text.Json.Serialization;

namespace Packata.Ossie;

public sealed record OssieAiContextDetails
{
    public string? Instructions { get; init; }

    public IReadOnlyList<string>? Synonyms { get; init; }

    public IReadOnlyList<string>? Examples { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
