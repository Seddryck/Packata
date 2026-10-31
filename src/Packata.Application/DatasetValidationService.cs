using Packata.Core.Contracts;
using Packata.Core.Execution;
using Packata.Core.Reading;
using Packata.ResourceReaders;

namespace Packata.Application;

/// <summary>
/// Binds existing descriptor/reader capabilities to structural plans and the shared runner.
/// Does not perform value validation, type inference, or execute authored quality rules.
/// </summary>
public sealed class DatasetValidationService(IDataEndpointReaderFactory readers, IInferenceStreamSource? metadataStreams = null)
{
    public async Task<RuleEvaluationReport> ValidateAsync(DataContract contract, string? assetId = null,
        RuleLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        cancellationToken.ThrowIfCancellationRequested();
        var selected = assetId is null ? contract.Assets.ToArray() : [ContractInferenceService.SelectAsset(contract, assetId)];
        var requiredAssets = RequiredAssets(selected);
        var inputs = new List<DatasetStructure>();
        foreach (var asset in contract.Assets.Where(a => requiredAssets.Contains(a.Id)))
            inputs.AddRange(await ReadAssetAsync(contract, asset, cancellationToken).ConfigureAwait(false));
        return await ValidateAsync(contract, selected, inputs, limits, cancellationToken).ConfigureAwait(false);
    }

    private static HashSet<string> RequiredAssets(IReadOnlyList<DataAsset> selected)
    {
        var required = selected.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var relationship in selected.SelectMany(a => a.Schema?.Relationships ?? [])) required.Add(relationship.TargetAsset);
        return required;
    }

    private async Task<IReadOnlyList<DatasetStructure>> ReadAssetAsync(DataContract contract, DataAsset asset, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        DataEndpointReadRequest request;
        try { request = DataPreviewService.SelectRequest(contract, asset) with { Schema = null }; }
        catch (DataPreviewSelectionException)
        {
            return [new(asset.Id, asset.Id, null, DiagnosticCode: "INPUT_BINDING_UNRESOLVED")];
        }
        if (request.Endpoint.Location is not PathLocation { Paths.Count: > 0 } paths)
            return [new(asset.Id, asset.Id, null, request.Endpoint.Id, DiagnosticCode: "STRUCTURE_SOURCE_UNSUPPORTED")];
        var inputs = new List<DatasetStructure>();
        foreach (var path in paths.Paths)
        {
            // Inspect each physical input independently; combined-reader projection must not hide differences.
            var single = request with { Endpoint = request.Endpoint with { Location = new PathLocation([path]) } };
            inputs.Add(await ReadInputAsync(asset, single, path, token).ConfigureAwait(false));
        }
        return inputs;
    }

    private async Task<DatasetStructure> ReadInputAsync(DataAsset asset, DataEndpointReadRequest request, string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var format = new DataFormatResolver().Resolve(request.Endpoint);
            return format.Name switch
            {
                "csv" or "tsv" or "psv" => await ReadDelimitedAsync(asset, request, path, token).ConfigureAwait(false),
                "parquet" => await ReadParquetAsync(asset, request, path, format, token).ConfigureAwait(false),
                _ => Unavailable(asset, request, path, "STRUCTURE_FORMAT_UNSUPPORTED")
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (NotSupportedException) { return Unavailable(asset, request, path, "STRUCTURE_CAPABILITY_UNSUPPORTED"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException or FormatException or ArgumentException)
        {
            // Provider messages can contain data or credentials. The shared runner emits a safe execution error.
            return Unavailable(asset, request, path, "STRUCTURE_READ_ERROR") with { ReadError = true };
        }
    }

    private async Task<DatasetStructure> ReadDelimitedAsync(DataAsset asset, DataEndpointReadRequest request, string path, CancellationToken token)
    {
        if (request.Endpoint.Format?.Options.TryGetValue("header", out var header) == true && header is false)
            return Unavailable(asset, request, path, "HEADER_IDENTITY_UNAVAILABLE");
        // Existing bounded operation, with no contract schema or value materialization.
        var sample = await new DataPreviewService(readers).ReadAsync(request,
            new(1, RequireUniqueColumnNames: false, StructureOnly: true), token).ConfigureAwait(false);
        var columns = sample.Columns.Select(column => new ObservedColumn(column.Name)).ToArray();
        if (columns.Length == 0) return Unavailable(asset, request, path, "STRUCTURE_UNAVAILABLE");
        return new(asset.Id, path, columns, request.Endpoint.Id);
    }

    private async Task<DatasetStructure> ReadParquetAsync(DataAsset asset, DataEndpointReadRequest request, string path,
        ResolvedDataFormat format, CancellationToken token)
    {
        if (metadataStreams is null || format.Compression is not null)
            return Unavailable(asset, request, path, "PARQUET_METADATA_SOURCE_UNAVAILABLE");
        // Reuse the metadata-only extractor; do not load row groups through a data reader.
        await using var stream = await metadataStreams.OpenAsync(asset, request.Endpoint, 256L * 1024 * 1024, null, token).ConfigureAwait(false);
        var result = await new Packata.ResourceReaders.Parquet.ParquetDataContractInference().InferAsync(stream, path, token).ConfigureAwait(false);
        var schema = result.Contract.Assets.Single().Schema ?? throw new InvalidDataException("Parquet metadata has no schema.");
        var observed = schema.Fields.Select(f => new ObservedColumn(f.Name, f.LogicalType)).ToArray();
        return new(asset.Id, path, observed, request.Endpoint.Id, TypesAvailable: true);
    }

    private static DatasetStructure Unavailable(DataAsset asset, DataEndpointReadRequest request, string path, string code)
        => new(asset.Id, path, null, request.Endpoint.Id, DiagnosticCode: code);

    /// <summary>Evaluate externally supplied inspection observations without opening data or requiring an inspector service.</summary>
    public static ValueTask<RuleEvaluationReport> ValidateAsync(DataContract contract, IReadOnlyList<DataAsset> assets,
        IReadOnlyList<DatasetStructure> structures, RuleLimits? limits = null, CancellationToken cancellationToken = default)
    {
        var compiler = new StructuralValidationCompiler(structures);
        var compilations = assets.Select(asset => compiler.Compile(asset, new(contract))).ToArray();
        return RuleRunner.ExecuteAsync(compilations, new(DateTimeOffset.UtcNow, Limits: limits), cancellationToken);
    }
}
