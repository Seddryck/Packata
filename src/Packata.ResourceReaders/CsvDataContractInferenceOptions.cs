using Packata.Core.Contracts;
using PocketCsvReader;

namespace Packata.ResourceReaders;

/// <summary>Configures CSV-to-canonical-contract inference.</summary>
public sealed record CsvDataContractInferenceOptions
{
    /// <summary>Sampling, culture, header, dialect, and resource options delegated to PocketCsvReader.</summary>
    public CsvInferenceOptions InferenceOptions { get; init; } = new() { InferDialect = true };

    /// <summary>Overrides the generated contract identity.</summary>
    public ContractIdentity? ContractIdentity { get; init; }

    /// <summary>Overrides the generated contract metadata.</summary>
    public ContractMetadata? ContractMetadata { get; init; }

    public string? AssetId { get; init; }
    public string? AssetName { get; init; }
    public string? AssetPhysicalName { get; init; }
    public string? AssetDescription { get; init; }
    public string? EndpointId { get; init; }
    public string? EndpointName { get; init; }
}
