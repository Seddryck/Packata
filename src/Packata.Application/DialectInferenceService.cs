using Packata.Core.Contracts;
using Packata.ResourceReaders;
using PocketCsvReader;

namespace Packata.Application;

public enum DialectUpdateFailure { SharedEndpoint, IncompleteSample, Ambiguous }
public sealed class DialectUpdateException(DialectUpdateFailure failure, string message) : InvalidOperationException(message)
{
    public DialectUpdateFailure Failure { get; } = failure;
}

public sealed record DialectInferenceResult(CsvInferenceResult Result, IReadOnlyList<string> SharedAssets)
{
    public ContractDocumentUpdate PrepareUpdate(EditableContract document, string assetId, string endpointId,
        bool allowSharedEndpoint = false, bool acceptAmbiguous = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SharedAssets.Count > 1 && !allowSharedEndpoint)
            throw new DialectUpdateException(DialectUpdateFailure.SharedEndpoint, "Shared endpoint update refused. Review the affected assets and explicitly allow a shared endpoint update.");
        if (Result.SampledRecordCount < 2 || Result.Diagnostics.Any(item => item.Code is "dialect.unresolved" or "records.inconsistent-width" or "sample.byte-limit"))
            throw new DialectUpdateException(DialectUpdateFailure.IncompleteSample, "Insufficient or incomplete sample for saving a dialect. Increase sampling limits or correct the resource and retry.");
        if (!acceptAmbiguous && Result.Diagnostics.Any(item => item.Code is "dialect.ambiguous" or "header.ambiguous"))
            throw new DialectUpdateException(DialectUpdateFailure.Ambiguous, "Ambiguous detection: review the candidates, select a header mode or explicitly accept ambiguity before saving.");
        var dialect = Result.Dialect;
        return DescriptorUpdateService.UpdateDialect(document, assetId, endpointId, new Dictionary<string, object?>
        {
            ["delimiter"] = dialect.Delimiter, ["quoteChar"] = dialect.QuoteChar,
            ["lineTerminator"] = dialect.LineTerminator, ["header"] = dialect.Header, ["headerRows"] = dialect.HeaderRows
        }, cancellationToken);
    }
}

public static class DialectInferenceService
{
    public static async Task<DialectInferenceResult> InferAsync(InferenceInput input, DataAsset asset, DataEndpoint endpoint,
        ContractInferenceOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ContractInferenceService.ValidateCsvEndpoint(asset, endpoint);
        var resolved = new DataFormatResolver().Resolve(endpoint);
        InferenceCapabilities.Validate(resolved.Name, options);
        var effective = endpoint with { Format = (endpoint.Format ?? new DataFormat(resolved.Name)) with
        { Name = resolved.Name, Compression = resolved.Compression } };
        await using var stream = await input.OpenAssetStreamAsync(asset, effective, cancellationToken, options.StagingMaximumBytes).ConfigureAwait(false);
        var result = new CsvInference(ContractInferenceService.CreateOptions(options, effective)).Infer(stream);
        cancellationToken.ThrowIfCancellationRequested();
        return new(result, input.Contract.Assets.Where(item => item.EndpointBindings.Any(binding => binding.EndpointId == endpoint.Id))
            .Select(item => item.Id).ToArray());
    }
}
