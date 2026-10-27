#nullable enable
using System.Collections;
using System.Data;
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Rules.Expressif;

/// <summary>One owned scan. No dataset materialization; all counters and readers are per run.</summary>
public sealed class QualityScan : IDisposable
{
    private readonly IDataReader _reader;
    private readonly RuleExecutionContext _context;
    private readonly DataSchema? _schema;
    private readonly CancellationToken _cancellation;
    private bool _enumerated;
    public long Population { get; private set; }
    public bool Complete { get; private set; }

    public QualityScan(IDataReader reader, RuleExecutionContext context, DataSchema? schema, CancellationToken cancellation)
        => (_reader, _context, _schema, _cancellation) = (reader, context, schema, cancellation);

    public IEnumerable<object?> Rows()
    {
        if (_enumerated) throw new IncompleteRuleInputException("REPEATED_SCAN");
        _enumerated = true;
        while (true)
        {
            _cancellation.ThrowIfCancellationRequested();
            if (!_reader.Read()) { Complete = true; yield break; }
            if (Population >= _context.Limits.MaxRows) throw new IncompleteRuleInputException("ROW_BUDGET");
            Population++;
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var index = 0; index < _reader.FieldCount; index++)
            {
                var physical = _reader.GetName(index);
                var logical = _schema?.Fields.SingleOrDefault(f => (f.PhysicalName ?? f.Name) == physical)?.Name ?? physical;
                row.Add(logical, _reader.IsDBNull(index) ? null : _reader.GetValue(index));
            }
            yield return row;
        }
    }

    public static object? Field(object? row, IReadOnlyList<string> path)
    {
        var value = row;
        foreach (var segment in path)
        {
            if (value is IReadOnlyDictionary<string, object?> readOnly && readOnly.TryGetValue(segment, out var next)) value = next;
            else if (value is IDictionary dictionary && dictionary.Contains(segment)) value = dictionary[segment];
            else throw new IncompleteRuleInputException("MISSING_FIELD");
        }
        return value;
    }

    public void Dispose() => _reader.Dispose();
}
