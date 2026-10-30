namespace Packata.Application;

public sealed class ContractAssetSelectionException(string assetId, bool ambiguous) : InvalidOperationException(
    ambiguous ? $"Asset identifier '{assetId}' is ambiguous." : $"Asset '{assetId}' was not found.")
{
    public bool Ambiguous { get; } = ambiguous;
}
