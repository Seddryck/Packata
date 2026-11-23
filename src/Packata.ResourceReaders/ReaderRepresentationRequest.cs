using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

/// <summary>Describes the representation to bind for one canonical field.</summary>
public sealed record ReaderRepresentationRequest(
    DataField Field,
    Type RuntimeType,
    string? ProfileName,
    IReadOnlyList<string> MissingValues)
{
    /// <summary>Identifies the spatial encoding.</summary>
    /// <value>The declared spatial encoding, when the field has one.</value>
    public SpatialEncoding? Encoding => Field.Spatial?.Encoding;

    /// <summary>Identifies the field format.</summary>
    /// <value>The declared field format.</value>
    public string? Format => Field.Format;
}
