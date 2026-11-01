using Packata.Arrow;
using Packata.Core.Reading;

namespace Packata.Http.Arrow;

public sealed class ArrowRepresentation : IAssetRepresentation
{
    public string MediaType => "application/vnd.apache.arrow.stream";
    public IReadOnlyList<string> Aliases => [];
    public RepresentationSupport Describe(ServedAsset asset)
    {
        var schema = ArrowSchemaMapper.Map(asset.Asset.Schema!);
        return new(true, "Arrow adapter flat primitive mappings; strict lossless conversion and declared timestamp units.",
            new { encoding = "arrow-ipc-schema", schemaIpcBase64 = Convert.ToBase64String(
                ArrowSchemaSerializer.SerializeAsync(schema).GetAwaiter().GetResult()) });
    }

    public async Task WriteAsync(ServedAsset asset, IDataEndpointReaderFactory readers, Stream destination,
        AssetStreamOptions options, CancellationToken cancellationToken)
    {
        try
        {
            await new ArrowIpcExporter(readers).WriteAsync(asset.Contract, asset.Asset.Id, asset.BindingIndex, destination,
                new() { BatchSize = options.BatchSize, MaxBufferedBytes = options.MaxBufferedBytes, LeaveOpen = true }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException error) when (error.InnerException is not null)
        {
            // Publish only a declared field identity, never provider or inner exception details.
            var field = asset.Asset.Schema!.Fields.FirstOrDefault(field =>
                error.Message.StartsWith($"Cannot convert field '{field.Name}' to ", StringComparison.Ordinal));
            if (field is not null) throw new AssetEncodingException(field.Name, field.LogicalType ?? "unknown", error);
            throw;
        }
    }
}
