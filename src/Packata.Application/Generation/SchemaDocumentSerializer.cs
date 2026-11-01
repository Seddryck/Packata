using Packata.Core.Contracts;
namespace Packata.Application.Generation;
public sealed record SchemaDocumentOptions(string? Name = null, string? Namespace = null);
public static class SchemaDocumentSerializer
{
    public static GenerationResult Serialize(DataAsset asset, string outputFormat, CancellationToken cancellationToken = default, SchemaDocumentOptions? options = null, string? profile = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (outputFormat is not ("avro" or "avro-idl" or "spark-json" or "iceberg-json" or "paimon-json")) throw new ArgumentException("Use an available structured serializer: avro, avro-idl, spark-json, iceberg-json or paimon-json.");
        if (options is not null && outputFormat is not ("avro" or "avro-idl"))
            return new(null, null, null, asset.Id, null, null, [new("GEN_OPTIONS", "Error", "$", "Schema naming options apply only to Avro export.")]);
        if (profile is not null && !SchemaCompatibilityProfiles.Supports(profile, outputFormat))
            return new(null, null, null, asset.Id, null, null, [new("GEN_OPTIONS", "Error", "$", "Unknown or incompatible export profile.")]);
        if (asset.Schema is null || asset.Schema.Fields.Count == 0) return new(null, null, null, asset.Id, null, null, [new("GEN_ASSET", "Error", "$.assets", "The asset has no usable schema.")]);
        return new StructuredSchemaExport(outputFormat, [], cancellationToken, options, profile).Export(asset);
    }
}
