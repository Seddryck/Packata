using Packata.Core.Contracts;
namespace Packata.Application.Generation;
public sealed record SchemaDocumentOptions(string? Name = null, string? Namespace = null);
public static class SchemaDocumentSerializer
{
    private static readonly HashSet<string> Formats = new(["avro", "avro-idl", "spark-json", "iceberg-json", "paimon-json"], StringComparer.Ordinal);

    public static GenerationResult Serialize(DataAsset asset, string outputFormat, CancellationToken cancellationToken = default, SchemaDocumentOptions? options = null, string? profile = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Formats.Contains(outputFormat)) throw new ArgumentException("Use an available structured serializer: avro, avro-idl, spark-json, iceberg-json or paimon-json.");
        if (ValidateOptions(outputFormat, options, profile) is { } error)
            return new(null, null, null, asset.Id, null, null, [new("GEN_OPTIONS", "Error", "$", error)]);
        if (asset.Schema is not { Fields.Count: > 0 }) return new(null, null, null, asset.Id, null, null, [new("GEN_ASSET", "Error", "$.assets", "The asset has no usable schema.")]);
        return new StructuredSchemaExport(outputFormat, [], cancellationToken, options, profile).Export(asset);
    }

    private static string? ValidateOptions(string outputFormat, SchemaDocumentOptions? options, string? profile)
    {
        if (options is not null && outputFormat is not ("avro" or "avro-idl")) return "Schema naming options apply only to Avro export.";
        if (profile is not null && !SchemaCompatibilityProfiles.Supports(profile, outputFormat)) return "Unknown or incompatible export profile.";
        return null;
    }
}
