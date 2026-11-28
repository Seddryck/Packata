using System.Collections.Concurrent;
using Packata.Core.Contracts;

namespace Packata.Core.Execution;

/// <summary>One explicitly bound raw input and its observed field prerequisites, keyed by logical field name.</summary>
public sealed record ConstraintInput(DataAsset Asset, string Source, IReadOnlyDictionary<string, string> Columns,
    IRuleDataSource? Data, string? EndpointId = null, string? DiagnosticCode = null, bool ReadError = false);

/// <summary>Result of declaration and observed-structure checks for one relationship.</summary>
public sealed record ConstraintRelationshipPrerequisite(string AssetId, int RelationshipIndex, string? DiagnosticCode = null);

/// <summary>Per-run scan state shared by constraint plans. Create a fresh instance for each execution.</summary>
public sealed class ConstraintEvaluationInput
{
    private readonly ConcurrentDictionary<ConstraintInput, Lazy<Task<ConstraintScan>>> _scans = new();

    internal async ValueTask<ConstraintScan> ScanAsync(ConstraintInput input, RuleLimits limits, CancellationToken token)
    {
        var scan = _scans.GetOrAdd(input, key => new(() => ReadAsync(key, limits, token)));
        try
        {
            return await scan.Value.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The shared scan was interrupted. Never reuse a partial snapshot.
            if (scan.Value.IsCanceled) _scans.TryRemove(new(input, scan));
            token.ThrowIfCancellationRequested();
            throw new IncompleteRuleInputException("TIME_BUDGET");
        }
    }

    private static async Task<ConstraintScan> ReadAsync(ConstraintInput input, RuleLimits limits, CancellationToken token)
    {
        if (input.ReadError) throw new IOException("The input could not be inspected.");
        if (input.DiagnosticCode is { } diagnostic) throw new IncompleteRuleInputException(diagnostic);
        if (input.Data is null) throw new IncompleteRuleInputException("RAW_INPUT_UNAVAILABLE");
        using var reader = await input.Data.OpenAsync(new(input.Asset.Id), input.EndpointId, token).ConfigureAwait(false);
        var fields = input.Asset.Schema!.Fields.Where(field => input.Columns.ContainsKey(field.Name)).ToArray();
        var parsers = fields.Select(field => new ConstraintValueParser(input.Asset.Schema!, field)).ToArray();
        var rows = new List<ConstraintCell[]>();
        int[]? ordinals = null;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (rows.Count >= limits.MaxRows)
                return new(fields, rows, "ROW_BUDGET");
            ordinals ??= FindOrdinals(input, fields, reader);
            var cells = new ConstraintCell[ordinals.Length];
            for (var i = 0; i < ordinals.Length; i++)
                cells[i] = parsers[i].Parse(reader.GetValue(ordinals[i]));
            rows.Add(cells);
        }

        return new(fields, rows, input.Data.IsComplete ? null : "RAW_INPUT_INCOMPLETE");
    }

    private static int[] FindOrdinals(ConstraintInput input, IEnumerable<DataField> fields, System.Data.IDataRecord reader)
    {
        var names = Enumerable.Range(0, reader.FieldCount).ToLookup(reader.GetName, StringComparer.Ordinal);
        return fields.Select(field =>
        {
            var matches = names[input.Columns[field.Name]].ToArray();
            if (matches.Length != 1) throw new IncompleteRuleInputException("RAW_COLUMN_BINDING_CHANGED");
            return matches[0];
        }).ToArray();
    }
}

internal sealed class ConstraintScan(DataField[] fields, IReadOnlyList<ConstraintCell[]> rows, string? diagnosticCode)
{
    public IReadOnlyList<ConstraintCell[]> Rows { get; } = rows;
    public string? DiagnosticCode { get; } = diagnosticCode;
    public int Ordinal(string field) => Array.FindIndex(fields, candidate => candidate.Name == field);
}
