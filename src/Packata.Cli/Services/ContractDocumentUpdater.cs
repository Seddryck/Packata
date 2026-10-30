using Packata.Application;
using Packata.Core.Contracts;

namespace Packata.Cli;

internal static class ContractDocumentUpdater
{
    internal static EditableContract Input(ContractDocument document) => new(document.OriginalText, document.Kind switch
    {
        ContractDocumentKind.DataPackageJson => DescriptorDocumentKind.DataPackageJson,
        ContractDocumentKind.DataPackageYaml => DescriptorDocumentKind.DataPackageYaml,
        ContractDocumentKind.OpenDataContractYaml => DescriptorDocumentKind.OpenDataContractYaml,
        _ => throw new ArgumentOutOfRangeException(nameof(document))
    });

    internal static ContractDocumentUpdate UpdateDialect(ContractDocument document, string assetId, string endpointId,
        IReadOnlyDictionary<string, object?> options) => DescriptorUpdateService.UpdateDialect(Input(document), assetId, endpointId, options);

    internal static ContractDocumentUpdate Update(ContractDocument document, DataAsset originalAsset, DataEndpoint originalEndpoint,
        DataAsset inferredAsset, DataEndpoint inferredEndpoint, CancellationToken cancellationToken = default) =>
        DescriptorUpdateService.Update(Input(document), originalAsset, originalEndpoint, inferredAsset, inferredEndpoint, cancellationToken);
}
