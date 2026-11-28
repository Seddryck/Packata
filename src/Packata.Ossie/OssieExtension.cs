using System.Text.Json;

namespace Packata.Ossie;

public sealed record OssieExtension
{
    public required string VendorName { get; init; }

    public required IReadOnlyDictionary<string, JsonElement> Data { get; init; }
}
