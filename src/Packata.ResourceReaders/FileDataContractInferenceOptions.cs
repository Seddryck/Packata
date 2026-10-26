using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

/// <summary>Identity overrides shared by file schema inferrers.</summary>
public record FileDataContractInferenceOptions
{
    public ContractIdentity? ContractIdentity { get; init; }
    public ContractMetadata? ContractMetadata { get; init; }
    public string? AssetId { get; init; }
    public string? AssetName { get; init; }
    public string? AssetPhysicalName { get; init; }
    public string? AssetDescription { get; init; }
    public string? EndpointId { get; init; }
    public string? EndpointName { get; init; }
}
