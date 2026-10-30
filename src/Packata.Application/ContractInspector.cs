using Packata.Core.Contracts;

namespace Packata.Application;

/// <summary>A parsed descriptor and its canonical projection, before host presentation.</summary>
public sealed record ContractInspection(ContractDocument Document, MappingResult<DataContract> Mapping);
public sealed record ContractInspectionOptions(string? AssetId = null);

/// <summary>Inspects descriptor declarations without acquiring or reading resource data.</summary>
public static class ContractInspector
{
    public static ContractInspection Inspect(string text, string format = "auto", Uri? baseUri = null,
        CancellationToken cancellationToken = default, ContractInspectionOptions? options = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Inspect(ContractSyntax.Parse(text), format, baseUri, cancellationToken, options);
    }

    public static ContractInspection Inspect(ContractSyntax syntax, string format = "auto", Uri? baseUri = null,
        CancellationToken cancellationToken = default, ContractInspectionOptions? options = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Inspect(ContractDocument.Parse(syntax, format, baseUri), cancellationToken, options);
    }

    public static ContractInspection Inspect(ContractDocument document, CancellationToken cancellationToken = default,
        ContractInspectionOptions? options = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mapped = document.Map();
        if (options?.AssetId is { } assetId && mapped.Value is { } contract)
        {
            var asset = ContractInferenceService.SelectAsset(contract, assetId);
            var endpoints = asset.EndpointBindings.Select(binding => binding.EndpointId).ToHashSet(StringComparer.Ordinal);
            mapped = mapped with { Value = contract with
            { Assets = [asset], Endpoints = contract.Endpoints.Where(endpoint => endpoints.Contains(endpoint.Id)).ToArray() } };
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(document, mapped);
    }
}
