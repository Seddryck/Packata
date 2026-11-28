using Packata.Core.Contracts;

namespace Packata.Core.Execution;

internal static class ConstraintKeyMeasurement
{
    public static async ValueTask<RuleMeasurement> UniqueAsync(ConstraintInput[] inputs, IReadOnlyList<string> fields, bool primary,
        RuleExecutionContext context, CancellationToken token)
    {
        RequireSupportedKeyFields(inputs, fields);
        var seen = new HashSet<QualityValue[]>(KeyComparer.Instance);
        var tally = new ConstraintTally(context.Limits, primary ? "unique non-null key tuple" : "unique key tuple");
        return await MeasureAsync(inputs, fields, tally, context, token, cells =>
        {
            var key = Key(cells);
            if (key is null) return primary;
            if (seen.Contains(key)) return true;
            Add(seen, key, context.Limits);
            return false;
        }).ConfigureAwait(false);
    }

    public static async ValueTask<RuleMeasurement> ForeignAsync(ConstraintInput[] sources, IReadOnlyList<string> fields,
        ConstraintInput[] targets, IReadOnlyList<string> targetFields, RuleExecutionContext context, CancellationToken token)
    {
        if (targets.Length == 0) return ConstraintCompiler.Incomplete("FOREIGN_KEY_TARGET_UNAVAILABLE");
        RequireSupportedKeyFields(sources, fields);
        var keys = new HashSet<QualityValue[]>(KeyComparer.Instance);
        var tally = new ConstraintTally(context.Limits, "matching target key tuple");
        await ReadLookupAsync(targets, targetFields, context, token, cells =>
        {
            if (Key(cells) is { } key) Add(keys, key, context.Limits);
        }).ConfigureAwait(false);
        return await MeasureAsync(sources, fields, tally, context, token, cells =>
            Key(cells) is { } key && !keys.Contains(key)).ConfigureAwait(false);
    }

    public static async ValueTask<RuleMeasurement> CategoryAsync(ConstraintInput source, string name, ConstraintInput[] targets,
        ValueDomain domain, RuleExecutionContext context, CancellationToken token)
    {
        var (fields, diagnostic) = CategoryFields(targets.FirstOrDefault()?.Asset.CategoryTable, domain.Reference!.EffectiveEncoding);
        if (diagnostic is not null) return ConstraintCompiler.Incomplete(diagnostic);

        var allowed = domain.AllowedValues?.ToHashSet();
        var lookup = new HashSet<ScalarValue>();
        await ReadLookupAsync(targets, fields, context, token, cells =>
        {
            if (IncludedCategory(cells[0], allowed) && cells[^1].Scalar is { } encoded)
                Add(lookup, encoded, context.Limits);
        }).ConfigureAwait(false);
        var tally = new ConstraintTally(context.Limits, "effective category values");
        return await MeasureAsync([source], [name], tally, context, token,
            cells => OutsideCategory(cells[0], lookup)).ConfigureAwait(false);
    }

    private static (string[] Fields, string? Diagnostic) CategoryFields(CategoryTableDefinition? table, ValueEncoding encoding)
    {
        if (table is not { ValueField.Segments.Count: 1 }) return ([], "CATEGORY_REFERENCE_UNAVAILABLE");
        var value = table.ValueField.Segments[0];
        if (encoding != ValueEncoding.Label) return ([value], null);
        return table.LabelField is { Segments.Count: 1 } label
            ? ([value, label.Segments[0]], null) : ([], "CATEGORY_LABEL_UNAVAILABLE");
    }

    private static bool IncludedCategory(ConstraintCell cell, HashSet<ScalarValue>? allowed)
        => cell.Scalar is { } value && (allowed is null || allowed.Contains(value));

    private static bool OutsideCategory(ConstraintCell cell, HashSet<ScalarValue> lookup)
        => cell.State == ConstraintCellState.Value && !IncludedCategory(cell, lookup);

    private static async Task ReadLookupAsync(ConstraintInput[] inputs, IReadOnlyList<string> fields,
        RuleExecutionContext context, CancellationToken token, Action<ConstraintCell[]> collect)
    {
        long population = 0;
        foreach (var input in inputs)
        {
            var scan = await ConstraintCompiler.ScanAsync(input, fields, context, token).ConfigureAwait(false);
            if (scan.DiagnosticCode is not null || scan.Rows.Count > context.Limits.MaxRows - population)
                throw new IncompleteRuleInputException(scan.DiagnosticCode ?? "LOOKUP_ROW_BUDGET");
            var ordinals = fields.Select(scan.Ordinal).ToArray();
            foreach (var row in scan.Rows)
            {
                token.ThrowIfCancellationRequested();
                var cells = ReadKey(row, ordinals);
                if (cells.Any(cell => cell.State == ConstraintCellState.Invalid))
                    throw new IncompleteRuleInputException("LOOKUP_VALUE_INVALID");
                collect(cells);
            }

            population += scan.Rows.Count;
        }
    }

    private static async ValueTask<RuleMeasurement> MeasureAsync(ConstraintInput[] inputs, IReadOnlyList<string> fields,
        ConstraintTally tally, RuleExecutionContext context, CancellationToken token, Func<ConstraintCell[], bool> violates)
    {
        long population = 0;
        string? diagnostic = null;
        foreach (var input in inputs)
        {
            var scan = await ConstraintCompiler.ScanAsync(input, fields, context, token).ConfigureAwait(false);
            diagnostic ??= scan.DiagnosticCode;
            var ordinals = fields.Select(scan.Ordinal).ToArray();
            for (var index = 0; index < scan.Rows.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                if (population >= context.Limits.MaxRows) return tally.Measure(population, "ROW_BUDGET");
                var cells = ReadKey(scan.Rows[index], ordinals);
                if (violates(cells)) tally.Record(input.Source, index, cells.Select(cell => cell.Raw));
                population++;
            }
        }

        return tally.Measure(population, diagnostic);
    }

    private static void RequireSupportedKeyFields(IEnumerable<ConstraintInput> inputs, IReadOnlyList<string> fields)
    {
        foreach (var input in inputs)
        {
            var schema = input.Asset.Schema!;
            if (schema.Fields.Any(field => fields.Contains(field.Name, StringComparer.Ordinal)
                && !new ConstraintValueParser(schema, field).Supported))
                throw new IncompleteRuleInputException("KEY_LOGICAL_TYPE_OR_FORMAT_UNSUPPORTED");
        }
    }

    private static ConstraintCell[] ReadKey(ConstraintCell[] row, int[] ordinals)
        => ordinals.Select(ordinal => row[ordinal]).ToArray();

    private static QualityValue[]? Key(ConstraintCell[] cells)
        => cells.Any(cell => cell.Value is null) ? null : cells.Select(cell => cell.Value!).ToArray();

    private static void Add<T>(HashSet<T> set, T item, RuleLimits limits)
    {
        if (set.Count >= limits.MaxStateEntries && !set.Contains(item))
            throw new IncompleteRuleInputException("KEY_STATE_BUDGET");
        set.Add(item);
    }

    private sealed class KeyComparer : IEqualityComparer<QualityValue[]>
    {
        public static KeyComparer Instance { get; } = new();

        public bool Equals(QualityValue[]? x, QualityValue[]? y)
        {
            if (x is null || y is null || x.Length != y.Length) return false;
            for (var i = 0; i < x.Length; i++)
                if (!TypedComparison.Equal(x[i], y[i])) return false;
            return true;
        }

        public int GetHashCode(QualityValue[] obj)
        {
            HashCode hash = default;
            foreach (var value in obj)
            {
                if (value.Value is ScalarValue { Kind: ScalarKind.Integer, Value: long integer })
                    hash.Add(ScalarValue.From((decimal)integer));
                else
                    hash.Add(value.Value);
            }

            return hash.ToHashCode();
        }
    }
}
