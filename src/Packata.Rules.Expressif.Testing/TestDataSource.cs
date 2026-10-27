using System.Collections.Concurrent;
using System.Data;
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Rules.Expressif.Testing;

internal sealed class TestDataSource(DataTable table, bool replayable = true, bool complete = true) : IRuleDataSource
{
    public ConcurrentBag<IDataReader> Readers { get; } = [];
    public string SnapshotId => "fixture";
    public bool IsReplayable => replayable;
    public bool IsComplete => complete;
    public ValueTask<IDataReader> OpenAsync(QualityTarget target, string? endpointId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IDataReader reader = table.CreateDataReader();
        Readers.Add(reader);
        return ValueTask.FromResult(reader);
    }
    public static TestDataSource Values(params object?[] values)
    {
        var table = new DataTable();
        table.Columns.Add("value", typeof(object));
        foreach (var value in values) table.Rows.Add(value ?? DBNull.Value);
        return new(table);
    }
}
