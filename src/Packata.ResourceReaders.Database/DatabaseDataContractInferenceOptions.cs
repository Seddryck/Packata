using DubUrl.Discovery;
using Packata.Core.Contracts;

namespace Packata.ResourceReaders.Database;

/// <summary>Overrides generated metadata for one discovered database asset.</summary>
public sealed record DatabaseAssetInferenceOptions(
    string? Id = null,
    string? Name = null,
    string? Description = null);

/// <summary>Configures database-to-canonical-contract inference.</summary>
public sealed record DatabaseDataContractInferenceOptions
{
    /// <summary>Metadata selection and completeness options delegated to DubUrl.</summary>
    /// <value>
    /// Metadata selection and completeness options delegated to DubUrl.
    /// </value>
    public DiscoveryOptions DiscoveryOptions { get; init; } = new() { AllowPartial = true };

    /// <summary>Overrides the generated contract identity.</summary>
    /// <value>
    /// Overrides the generated contract identity.
    /// </value>
    public ContractIdentity? ContractIdentity { get; init; }

    /// <summary>Overrides the generated contract metadata.</summary>
    /// <value>
    /// Overrides the generated contract metadata.
    /// </value>
    public ContractMetadata? ContractMetadata { get; init; }

    /// <summary>
    /// Overrides assets by their fully qualified physical identity, such as
    /// <c>catalog.database.schema.table</c> or <c>schema.table</c>.
    /// </summary>
    /// <value>
    /// Overrides assets by their fully qualified physical identity, such as
    /// <c>catalog.database.schema.table</c> or <c>schema.table</c>.
    /// </value>
    public IReadOnlyDictionary<string, DatabaseAssetInferenceOptions> Assets { get; init; } =
        new Dictionary<string, DatabaseAssetInferenceOptions>(StringComparer.Ordinal);

    public string? EndpointId { get; init; }
    public string? EndpointName { get; init; }
    public string? Environment { get; init; }
}
