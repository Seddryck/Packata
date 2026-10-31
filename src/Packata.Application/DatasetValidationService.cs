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
        var inputs = new List<DatasetStructure>();
        var requiredAssets = selected.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var relationship in selected.SelectMany(a => a.Schema?.Relationships ?? [])) requiredAssets.Add(relationship.TargetAsset);
        foreach (var asset in contract.Assets.Where(a => requiredAssets.Contains(a.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DataEndpointReadRequest request;
            try { request = DataPreviewService.SelectRequest(contract, asset) with { Schema = null }; }
            catch (DataPreviewSelectionException)
            {
                inputs.Add(new(asset.Id, asset.Id, null, DiagnosticCode: "INPUT_BINDING_UNRESOLVED"));
                continue;
            }
            if (request.Endpoint.Location is not PathLocation { Paths.Count: > 0 } paths)
            {
                inputs.Add(new(asset.Id, asset.Id, null, request.Endpoint.Id, DiagnosticCode: "STRUCTURE_SOURCE_UNSUPPORTED"));
                continue;
            }
            foreach (var path in paths.Paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Inspect each physical input independently; never let combined-reader projection hide differences.
                var single = request with { Endpoint = request.Endpoint with { Location = new PathLocation([path]) } };
                try
                {
                    var format = new DataFormatResolver().Resolve(single.Endpoint);
                    var delimited = format.Name is "csv" or "tsv" or "psv";
                    if (!delimited && format.Name != "parquet")
                    {
                        inputs.Add(new(asset.Id, path, null, request.Endpoint.Id, DiagnosticCode: "STRUCTURE_FORMAT_UNSUPPORTED"));
                        continue;
                    }
                    if (delimited && single.Endpoint.Format?.Options.TryGetValue("header", out var header) == true && header is false)
                    {
                        inputs.Add(new(asset.Id, path, null, request.Endpoint.Id, DiagnosticCode: "HEADER_IDENTITY_UNAVAILABLE"));
                        continue;
                    }
                    if (!delimited)
                    {
                        if (metadataStreams is null || format.Compression is not null)
                        {
                            inputs.Add(new(asset.Id, path, null, request.Endpoint.Id, DiagnosticCode: "PARQUET_METADATA_SOURCE_UNAVAILABLE"));
                            continue;
                        }
                        // Reuse the metadata-only Parquet schema extractor; do not load row groups through a data reader.
                        await using var stream = await metadataStreams.OpenAsync(asset, single.Endpoint, 256L * 1024 * 1024,
                            null, cancellationToken).ConfigureAwait(false);
                        var result = await new Packata.ResourceReaders.Parquet.ParquetDataContractInference()
                            .InferAsync(stream, path, cancellationToken).ConfigureAwait(false);
                        var observed = result.Contract.Assets.Single().Schema!.Fields
                            .Select(f => new ObservedColumn(f.Name, f.LogicalType)).ToArray();
                        inputs.Add(new(asset.Id, path, observed, request.Endpoint.Id, TypesAvailable: true));
                        continue;
                    }
                    // Existing bounded reader/preview operation, with no contract schema or name projection.
                    // CSV values remain text; the first row is only used to initialize streaming header metadata.
                    var sample = await new DataPreviewService(readers).ReadAsync(single,
                        new(1, RequireUniqueColumnNames: false, StructureOnly: true), cancellationToken).ConfigureAwait(false);
                    var columns = sample.Columns.Select(column => new ObservedColumn(column.Name)).ToArray();
                    inputs.Add(new(asset.Id, path, columns.Length == 0 ? null : columns, request.Endpoint.Id,
                        TypesAvailable: !delimited, DiagnosticCode: columns.Length == 0 ? "STRUCTURE_UNAVAILABLE" : null));
                }
                catch (OperationCanceledException) { throw; }
                catch (NotSupportedException)
                {
                    inputs.Add(new(asset.Id, path, null, request.Endpoint.Id, DiagnosticCode: "STRUCTURE_CAPABILITY_UNSUPPORTED"));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException or FormatException or ArgumentException)
                {
                    // Provider messages can contain data or credentials. The shared runner emits a safe execution error.
                    inputs.Add(new(asset.Id, path, null, request.Endpoint.Id, DiagnosticCode: "STRUCTURE_READ_ERROR", ReadError: true));
                }
            }
        }
        return await ValidateAsync(contract, selected, inputs, limits, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluate externally supplied inspection observations without opening data or requiring an inspector service.</summary>
    public static ValueTask<RuleEvaluationReport> ValidateAsync(DataContract contract, IReadOnlyList<DataAsset> assets,
        IReadOnlyList<DatasetStructure> structures, RuleLimits? limits = null, CancellationToken cancellationToken = default)
    {
        var compiler = new StructuralValidationCompiler(structures);
        var compilations = assets.Select(asset => compiler.Compile(asset, new(contract))).ToArray();
        return RuleRunner.ExecuteAsync(compilations, new(DateTimeOffset.UtcNow, Limits: limits), cancellationToken);
    }
}
