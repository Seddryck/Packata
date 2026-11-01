using Packata.Core.Contracts;
namespace Packata.Application.Generation;
public sealed record SchemaDocumentOptions(string? Name = null, string? Namespace = null);
public static class SchemaDocumentSerializer
{
    public static GenerationResult Serialize(DataAsset asset, string outputFormat, CancellationToken cancellationToken = default, SchemaDocumentOptions? options = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (outputFormat is not ("avro" or "avro-idl" or "spark-json")) throw new ArgumentException("Use an available structured serializer: avro, avro-idl or spark-json.");
        if (asset.Schema is null || asset.Schema.Fields.Count == 0) return new(null, null, null, asset.Id, null, null, [new("GEN_ASSET", "Error", "$.assets", "The asset has no usable schema.")]);
        return new StructuredSchemaExport(outputFormat, [], cancellationToken, options).Export(asset);
    }
}
