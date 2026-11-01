#nullable enable
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Rules.Expressif;

public sealed class LibraryQualityCompiler(IMetricCatalog? catalog = null) : IRuleCompiler<DataQualityRule>
{
    private readonly IMetricCatalog _catalog = catalog ?? new OdcsMetricCatalog();
    public CompilationResult Compile(DataQualityRule declaration, RuleCompilationContext context)
    {
        var rule = declaration.Snapshot();
        if (rule.Definition is not LibraryQuality library) return CompilationResult.Unsupported(rule, "A Library quality definition is required.");
        var definition = _catalog.Resolve(library.Catalog, library.Version, library.Metric);
        if (definition is null) return CompilationResult.Unsupported(rule, "The requested catalog/version/metric has no registered implementation.");
        var findings = QualityCompilation.Validate(rule).ToList();
        MetricValidation.Validate(rule, library, definition, findings);
        if (findings.Count > 0) return new([], findings.AsReadOnly());
        try
        {
            var schema = context.Contract?.Assets.SingleOrDefault(a => a.Id == rule.Target!.AssetId)?.Schema;
            var measurement = new MetricMeasurement(rule, library, definition, schema, context.EndpointId);
            return new([new MeasuredExecutableRule(QualityCompilation.Descriptor(rule, "expressif", $"{definition.Catalog}/{definition.Version};{definition.ProgramVersion};{PreparedQualityProgram.BackendVersion}", context.EndpointId),
                new(QualityInputScope.Dataset), measurement, new ExpectationAssertionPlan(rule.Expectations))], []);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new([], [new(rule.ExecutionId, CompilationFindingKind.Unsupported, "METRIC_PROGRAM", "The catalog program cannot be prepared by the pinned backend.")]);
        }
    }

    private sealed class MetricMeasurement : IMeasurementPlan
    {
        private readonly DataQualityRule _rule;
        private readonly LibraryQuality _library;
        private readonly MetricDefinition _definition;
        private readonly DataSchema? _schema;
        private readonly string? _endpointId;
        private readonly PreparedQualityProgram _projection;
        private readonly PreparedQualityProgram _finalize;
        public MetricMeasurement(DataQualityRule rule, LibraryQuality library, MetricDefinition definition, DataSchema? schema, string? endpointId)
        {
            (_rule, _library, _definition, _schema, _endpointId) = (rule, library, definition, schema, endpointId);
            _projection = new(definition.Program.Projection, QualityProgramKind.Expression);
            _finalize = new(library.Unit == "percent" ? definition.Program.PercentFinalization : definition.Program.RowsFinalization, QualityProgramKind.Expression);
            _ = definition.Program.Selection(library.Arguments);
        }

        public async ValueTask<RuleMeasurement> MeasureAsync(RuleExecutionContext context, CancellationToken cancellationToken)
        {
            if (context.Data is null) throw new IncompleteRuleInputException("MISSING_DATA");
            var reader = await context.Data.OpenAsync(_rule.Target!, _endpointId, cancellationToken).ConfigureAwait(false);
            using var scan = new QualityScan(reader, context, _schema, cancellationToken);
            if (_library.Arguments.Values.Where(v => v.Kind == QualityValueKind.List)
                .Sum(v => (long)((IReadOnlyList<QualityValue>)v.Value!).Count) > context.Limits.MaxStateEntries)
                throw new IncompleteRuleInputException("ARGUMENT_STATE_BUDGET");
            var bindings = _library.Arguments.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            bindings.Add("selected", QualityValue.From(true));
            var project = _projection.Bind(bindings, context.ObservationTime);
            var select = _definition.Program.Selection(_library.Arguments);
            var numerator = Reduce(scan, project, select, context.Limits, cancellationToken);
            return Finalize(numerator, scan, context.ObservationTime);
        }

        private long Reduce(QualityScan scan, Func<object?, object?> project, global::Expressif.Predicates.IPredicate select,
            RuleLimits limits, CancellationToken cancellationToken)
        {
            var seen = new HashSet<ScalarKey>();
            long numerator = 0;
            foreach (var row in scan.Rows())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = _rule.Target!.FieldPath.Count > 0 ? QualityScan.Field(row, _rule.Target.FieldPath) : row;
                var projected = project(input);
                if (_definition.Program.Reduction == MetricReduction.CountExcessOccurrences)
                    numerator += CountOccurrence(seen, GroupKey(row, projected), limits.MaxStateEntries);
                else if (select.Evaluate(projected)) numerator++;
            }
            return numerator;
        }

        private ScalarKey GroupKey(object? row, object? projected)
        {
            var values = _rule.Target!.FieldPath.Count > 0 ? new[] { projected }
                : ((IReadOnlyList<QualityValue>)_library.Arguments["properties"].Value!).Select(p => QualityScan.Field(row, [(string)p.ToObject()!])).ToArray();
            return new(values.Select(ScalarValue.From).ToArray());
        }

        private static int CountOccurrence(HashSet<ScalarKey> seen, ScalarKey key, int limit)
        {
            if (seen.Contains(key)) return 1;
            if (seen.Count >= limit) throw new IncompleteRuleInputException("STATE_BUDGET");
            seen.Add(key);
            return 0;
        }

        private RuleMeasurement Finalize(long numerator, QualityScan scan, DateTimeOffset observationTime)
        {
            var evidence = new RuleEvidence[] { new("numerator", QualityValue.From(numerator)), new("denominator", QualityValue.From(scan.Population)) };
            if (_library.Unit == "percent" && scan.Population == 0)
                return new(null, _library.Unit, 0, false, evidence, [new("ZERO_DENOMINATOR", "An empty population has no defined percentage.")]);
            var finalize = _finalize.Bind(new Dictionary<string, QualityValue>
                { ["numerator"] = QualityValue.From((decimal)numerator), ["denominator"] = QualityValue.From((decimal)scan.Population) }, observationTime);
            var value = QualityValue.From(finalize(null));
            return new(value, _library.Unit, scan.Population, scan.Complete, evidence);
        }
    }

    private sealed class ScalarKey(ScalarValue[] values) : IEquatable<ScalarKey>
    {
        private ScalarValue[] Values => values;
        public bool Equals(ScalarKey? other) => other is not null && values.AsSpan().SequenceEqual(other.Values);
        public override bool Equals(object? obj) => obj is ScalarKey other && Equals(other);
        public override int GetHashCode()
        {
            var hash = default(HashCode);
            foreach (var value in values) hash.Add(value);
            return hash.ToHashCode();
        }
    }
}
