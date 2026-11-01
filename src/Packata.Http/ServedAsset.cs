using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.Http;

/// <summary>Immutable selection of one asset and binding. No source data is opened by selection.</summary>
public sealed class ServedAsset
{
    public DataContract Contract { get; }
    public DataAsset Asset { get; }
    public int BindingIndex { get; }
    public DataEndpointReadRequest Request { get; }
    public string Id { get; }

    public ServedAsset(DataContract contract, string assetId, int bindingIndex)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var assets = contract.Assets.Where(asset => asset.Id == assetId).ToArray();
        if (assets.Length != 1) throw new ArgumentException("Asset ID must resolve uniquely.", nameof(assetId));
        Asset = assets[0];
        if (Asset.Schema is null) throw new ArgumentException("A served asset must declare a schema.", nameof(assetId));
        if (bindingIndex < 0 || bindingIndex >= Asset.EndpointBindings.Count)
            throw new ArgumentOutOfRangeException(nameof(bindingIndex));
        var binding = Asset.EndpointBindings[bindingIndex];
        var endpoints = contract.Endpoints.Where(endpoint => endpoint.Id == binding.EndpointId).ToArray();
        if (endpoints.Length != 1) throw new ArgumentException("Endpoint ID must resolve uniquely.", nameof(bindingIndex));
        Contract = contract;
        BindingIndex = bindingIndex;
        Request = new(endpoints[0], Asset.Schema, binding.AssetPath ?? Asset.PhysicalName ?? Asset.Name, contract);
        Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]
            { contract.Identity.Id, contract.Identity.Version, assetId, binding.EndpointId, binding.AssetPath })))).ToLowerInvariant();
    }
}

public sealed record RepresentationSupport(bool Supported, string? Limitation = null, object? Schema = null);

/// <summary>A safe, contract-field diagnostic for an unsupported or lossy runtime value.</summary>
public sealed class AssetEncodingException(string field, string logicalType, Exception innerException)
    : Exception($"Field '{field}' cannot be represented losslessly as {logicalType}.", innerException)
{
    public string Field { get; } = field;
    public string LogicalType { get; } = logicalType;
}

/// <summary>Encoders must validate without consuming data in Describe. WriteAsync owns every reader
/// it opens, but never owns the destination. It must await writes and release buffers incrementally.</summary>
public interface IAssetRepresentation
{
    string MediaType { get; }
    IReadOnlyList<string> Aliases { get; }
    RepresentationSupport Describe(ServedAsset asset);
    Task WriteAsync(ServedAsset asset, IDataEndpointReaderFactory readers, Stream destination,
        AssetStreamOptions options, CancellationToken cancellationToken);
}

public sealed record AssetStreamOptions
{
    public int BatchSize { get; init; } = 128;
    public int MaxBufferedBytes { get; init; } = 1024 * 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BatchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxBufferedBytes);
    }
}
