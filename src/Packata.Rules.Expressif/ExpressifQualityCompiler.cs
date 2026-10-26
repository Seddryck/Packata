#nullable enable
using Packata.Core.Contracts;
using Packata.Core.Execution;

namespace Packata.Rules.Expressif;

public sealed class ExpressifQualityCompiler : IRuleCompiler<DataQualityRule>
{
    public CompilationResult Compile(DataQualityRule declaration, RuleCompilationContext context)
    {
        var rule = declaration.Snapshot();
        if (rule.Definition is not ExpressifQuality definition) return CompilationResult.Unsupported(rule, "An explicit Expressif definition is required.");
        var findings = QualityCompilation.Validate(rule).ToList();
        ValidateDefinition(rule, definition, findings);
        if (findings.Count > 0) return new([], findings.AsReadOnly());
        try
        {
            var program = new PreparedQualityProgram(definition.Program, definition.ProgramKind);
            var schema = context.Contract?.Assets.SingleOrDefault(a => a.Id == rule.Target!.AssetId)?.Schema;
            var descriptor = QualityCompilation.Descriptor(rule, "expressif", PreparedQualityProgram.BackendVersion, context.EndpointId);
            return new([new MeasuredExecutableRule(descriptor, new(definition.Scope),
                new ExpressifMeasurement(rule, definition, program, schema, context.EndpointId),
                new ScopedAssertion(rule.Expectations, definition))], []);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new([], [new(rule.ExecutionId, exception is NotSupportedException ? CompilationFindingKind.Unsupported : CompilationFindingKind.Invalid,
                "EXPRESSIF_PROGRAM", "The program is invalid or uses unsupported quality operations.")]);
        }
    }

    private static void ValidateDefinition(DataQualityRule rule, ExpressifQuality definition, List<CompilationFinding> findings)
    {
        if (definition.PayloadVersion != "1" || !Enum.IsDefined(definition.Scope) || !Enum.IsDefined(definition.ProgramKind))
            findings.Add(new(rule.ExecutionId, CompilationFindingKind.Unsupported, "PAYLOAD_VERSION", "Unsupported Expressif payload version, scope or kind."));
        if (definition.ProgramKind == QualityProgramKind.Predicate && rule.Expectations.Any(e => e.Operator is not (ExpectationOperator.Equal or ExpectationOperator.NotEqual)
            || e.Operand.ToObject() is not bool))
            findings.Add(new(rule.ExecutionId, CompilationFindingKind.Invalid, "PREDICATE_EXPECTATION", "A predicate accepts only boolean equality expectations."));
        if (definition.Scope == QualityInputScope.Value && rule.Target?.FieldPath.Count == 0)
            findings.Add(new(rule.ExecutionId, CompilationFindingKind.MissingBinding, "MISSING_FIELD_TARGET", "Value scope requires an explicit field target."));
    }

    private sealed class ScopedAssertion(IReadOnlyList<QualityExpectation> expectations, ExpressifQuality definition) : IAssertionPlan
    {
        public IReadOnlyList<QualityExpectation> Expectations { get; } = expectations;
        public RuleOutcome Evaluate(QualityValue value) => definition.Scope is QualityInputScope.Row or QualityInputScope.Value
            ? new ExpectationAssertionPlan([], true).Evaluate(value)
            : new ExpectationAssertionPlan(Expectations, definition.ProgramKind == QualityProgramKind.Predicate).Evaluate(value);
    }

    private sealed class ExpressifMeasurement(DataQualityRule rule, ExpressifQuality definition,
        PreparedQualityProgram program, DataSchema? schema, string? endpointId) : IMeasurementPlan
    {
        public async ValueTask<RuleMeasurement> MeasureAsync(RuleExecutionContext context, CancellationToken cancellationToken)
        {
            var evaluate = Bind(context);
            if (definition.Scope == QualityInputScope.Observation)
                return EvaluateSingle(context.Observation ?? throw new IncompleteRuleInputException("MISSING_OBSERVATION"), evaluate);
            if (context.Value is { } value && definition.Scope is QualityInputScope.Value or QualityInputScope.Row)
                return EvaluateSingle(value, evaluate);
            if (context.Data is null) throw new IncompleteRuleInputException("MISSING_DATA");
            var reader = await context.Data.OpenAsync(rule.Target!, endpointId, cancellationToken).ConfigureAwait(false);
            using var scan = new QualityScan(reader, context, schema, cancellationToken);
            if (definition.Scope == QualityInputScope.Dataset)
            {
                var measured = Scalar(evaluate(scan.Rows().Select(row => { program.ValidateFields(row); return row; })));
                return new(measured, Population: scan.Population, Complete: scan.Complete);
            }
            return MeasureRows(scan, evaluate, cancellationToken);
        }

        private Func<object?, object?> Bind(RuleExecutionContext context)
        {
            var bindings = definition.Bindings.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            if (context.Bindings.Any(pair => !bindings.TryAdd(pair.Key, pair.Value)))
                throw new ArgumentException("Conflicting declaration and runtime bindings.");
            return program.Bind(bindings, context.ObservationTime);
        }

        private RuleMeasurement MeasureRows(QualityScan scan, Func<object?, object?> evaluate, CancellationToken cancellationToken)
        {
            long violations = 0;
            foreach (var row in scan.Rows())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = definition.Scope == QualityInputScope.Value ? QualityScan.Field(row, rule.Target!.FieldPath) : row;
                var measured = Scalar(evaluate(input));
                // Apply each measurement's expectations, then reduce violations across the whole population.
                var assertion = new ExpectationAssertionPlan(rule.Expectations, definition.ProgramKind == QualityProgramKind.Predicate);
                var outcome = assertion.Evaluate(measured);
                if (outcome == RuleOutcome.Inconclusive) return new(null, Population: scan.Population, Complete: false,
                    Diagnostics: [new("MISSING_EXPECTATION", "Row/value expression measurements require explicit expectations.")]);
                if (outcome == RuleOutcome.Failed) violations++;
            }
            if (scan.Population == 0) return new(null, Population: 0, Complete: false, Diagnostics: [new("EMPTY_INPUT", "No values were evaluated.")]);
            return new(QualityValue.From(violations == 0), "verdict", scan.Population, scan.Complete,
                [new("violations", QualityValue.From(violations)), new("population", QualityValue.From(scan.Population))]);
        }

        private RuleMeasurement EvaluateSingle(QualityValue value, Func<object?, object?> evaluate)
        {
            var measured = Scalar(evaluate(value.ToObject()));
            if (definition.Scope is QualityInputScope.Value or QualityInputScope.Row)
            {
                var outcome = new ExpectationAssertionPlan(rule.Expectations, definition.ProgramKind == QualityProgramKind.Predicate).Evaluate(measured);
                return outcome == RuleOutcome.Inconclusive ? new(null, Population: 1, Complete: false)
                    : new(QualityValue.From(outcome == RuleOutcome.Passed), "verdict", 1);
            }
            return new(measured, Population: 1);
        }
        private static QualityValue Scalar(object? value)
        {
            var result = QualityValue.From(value);
            if (result.Kind is QualityValueKind.List or QualityValueKind.Object) throw new ArgumentException("A program measurement must be scalar.");
            return result;
        }
    }
}
