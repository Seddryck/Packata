using System.Data;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.Core.Execution;

/// <summary>Reopens an explicitly selected reader request. The caller guarantees snapshot stability and replayability.</summary>
public sealed class ReaderRuleDataSource(IDataEndpointReaderFactory factory, string assetId,
    DataEndpointReadRequest request, string snapshotId, bool isReplayable = true, bool isComplete = true) : IRuleDataSource
{
    public string SnapshotId { get; } = snapshotId;
    public bool IsReplayable { get; } = isReplayable;
    public bool IsComplete { get; } = isComplete;
    public ValueTask<IDataReader> OpenAsync(QualityTarget target, string? endpointId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (target.AssetId != assetId || (endpointId is not null && endpointId != request.Endpoint.Id))
            throw new IncompleteRuleInputException("INPUT_BINDING_MISMATCH");
        return factory.OpenAsync(request, cancellationToken);
    }
}
