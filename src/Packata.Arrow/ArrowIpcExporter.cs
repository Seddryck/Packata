using Apache.Arrow.Ipc;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.Arrow;

public sealed record ArrowIpcExportOptions
{
    public int BatchSize { get; init; } = 1024;
    public int MaxBufferedBytes { get; init; } = 16 * 1024 * 1024;
    /// <summary>Keep the destination open on success, cancellation, or failure. Defaults to true.</summary>
    public bool LeaveOpen { get; init; } = true;
}

/// <summary>Exports one explicitly selected contract asset and binding as an Arrow IPC stream.</summary>
public sealed class ArrowIpcExporter(IDataEndpointReaderFactory readers)
{
    /// <summary>Selection and schema validation happen before acquiring the destination.
    /// Once validated, LeaveOpen controls destination ownership, including when opening the source fails.
    /// The opened reader and every batch are always disposed. Writes are incremental; failures and
    /// cancellation propagate and may leave a partial stream without an end marker. Synchronous source
    /// reads cannot be interrupted in progress. The destination is flushed on success.</summary>
    public async Task WriteAsync(DataContract contract, string assetId, int bindingIndex, Stream destination,
        ArrowIpcExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("Destination must be writable.", nameof(destination));
        options ??= new();
        var batches = new ArrowBatchOptions
        {
            BatchSize = options.BatchSize, MaxBufferedBytes = options.MaxBufferedBytes, LeaveOpen = true
        };
        batches.Validate();
        var request = SelectRequest(contract, assetId, bindingIndex);
        var schema = ArrowSchemaMapper.Map(request.Schema!);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = await readers.OpenAsync(request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            using var writer = new ArrowStreamWriter(destination, schema, leaveOpen: true);
            await writer.WriteStartAsync(cancellationToken).ConfigureAwait(false);
            foreach (var batch in ArrowRecordBatches.Read(reader, request.Schema!, batches, cancellationToken))
            {
                using (batch)
                    await writer.WriteRecordBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteEndAsync(cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!options.LeaveOpen) await destination.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Creates a new file containing an IPC stream (not the Arrow random-access file format).
    /// Existing files are never overwritten. Failure may leave a partial file; the file is always closed.</summary>
    public async Task WriteFileAsync(DataContract contract, string assetId, int bindingIndex, string path,
        ArrowIpcExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Validate before creating a file, using the same rules as stream exports.
        options ??= new();
        new ArrowBatchOptions { BatchSize = options.BatchSize, MaxBufferedBytes = options.MaxBufferedBytes }.Validate();
        var request = SelectRequest(contract, assetId, bindingIndex);
        _ = ArrowSchemaMapper.Map(request.Schema!);
        cancellationToken.ThrowIfCancellationRequested();
        await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.Asynchronous);
        await WriteAsync(contract, assetId, bindingIndex, destination, options with { LeaveOpen = true }, cancellationToken)
            .ConfigureAwait(false);
    }

    private static DataEndpointReadRequest SelectRequest(DataContract contract, string assetId, int bindingIndex)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        var assets = contract.Assets.Where(asset => asset.Id == assetId).ToArray();
        if (assets.Length != 1)
            throw new ArgumentException("The selected asset must resolve uniquely by ID.", nameof(assetId));
        var asset = assets[0];
        if (asset.Schema is null) throw new ArgumentException("The selected asset must declare a schema.", nameof(assetId));
        if (bindingIndex < 0 || bindingIndex >= asset.EndpointBindings.Count)
            throw new ArgumentOutOfRangeException(nameof(bindingIndex));
        var binding = asset.EndpointBindings[bindingIndex];
        var endpoints = contract.Endpoints.Where(endpoint => endpoint.Id == binding.EndpointId).ToArray();
        if (endpoints.Length != 1)
            throw new ArgumentException("The selected binding's endpoint must resolve uniquely.", nameof(bindingIndex));
        return new(endpoints[0], asset.Schema, binding.AssetPath ?? asset.PhysicalName ?? asset.Name, contract);
    }
}
