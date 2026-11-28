using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Packata.Core.Contracts;

namespace Packata.Core.Execution;

/// <summary>Compiles canonical value requirements over explicitly bound inputs and structural prerequisites.</summary>
public sealed class ConstraintCompiler(IEnumerable<ConstraintInput> inputs,
    IEnumerable<ConstraintRelationshipPrerequisite>? relationships = null) : IRuleCompiler<DataAsset>
{
    private readonly ConstraintInput[] _inputs = inputs.ToArray();
    private readonly ConstraintRelationshipPrerequisite[] _relationships = relationships?.ToArray() ?? [];

    public CompilationResult Compile(DataAsset declaration, RuleCompilationContext context)
    {
        if (declaration.Schema is not { } schema)
            return new([], [new($"constraint:{declaration.Id}", CompilationFindingKind.Invalid, "SCHEMA_MISSING", "An explicit schema is required for constraint validation.")]);

        var inputs = Inputs(declaration.Id).Where(input => context.EndpointId is null || input.EndpointId == context.EndpointId).ToArray();
        if (inputs.Length == 0)
            return new([], [new($"constraint:{declaration.Id}", CompilationFindingKind.MissingBinding, "RAW_INPUT_UNAVAILABLE", "Bind the asset's raw input before evaluating constraints.")]);

        var prefix = $"constraint:{declaration.Id}";
        var plans = FieldPlans(schema, inputs, prefix)
            .Concat(UniquePlans(schema, inputs, prefix))
            .Concat(ForeignPlans(declaration.Id, schema, inputs, prefix))
            .ToArray();
        return new(Array.AsReadOnly(plans), []);
    }

    private ConstraintInput[] Inputs(string assetId) => _inputs.Where(input => input.Asset.Id == assetId).ToArray();

    private static bool Applies(ConstraintInput input, DataField field)
        => input.DiagnosticCode is not null || input.ReadError || field.Required
            || input.Columns.ContainsKey(field.Name) || input.AmbiguousFields?.Contains(field.Name) == true;

    private IEnumerable<IExecutableRule> FieldPlans(DataSchema schema, ConstraintInput[] inputs, string prefix)
    {
        for (var index = 0; index < inputs.Length; index++)
        {
            var input = inputs[index];
            foreach (var field in schema.Fields.Where(field => Applies(input, field)))
            {
                foreach (var plan in FieldPlans(schema, input, field, $"{prefix}:{index}:{field.Name}"))
                    yield return plan;
            }
        }
    }

    private static IEnumerable<IExecutableRule> UniquePlans(DataSchema schema, ConstraintInput[] inputs, string prefix)
    {
        foreach (var field in schema.Fields.Where(field => field.Constraints.Any(c => c is { Kind: "unique", Value: true })))
        {
            var applicable = inputs.Where(input => Applies(input, field)).ToArray();
            if (applicable.Length > 0)
                yield return KeyPlan(applicable, [field.Name], $"{prefix}:unique:{field.Name}");
        }

        var primary = schema.PrimaryKey.Count > 0 ? schema.PrimaryKey : schema.Fields
            .Where(field => field.Constraints.Any(c => c is { Kind: "primaryKey", Value: true })).Select(field => field.Name).ToArray();
        if (primary.Count > 0)
            yield return KeyPlan(inputs, primary, $"{prefix}:primaryKey", primaryKey: true);
        if (schema.Extensions.TryGetNamespace("datapackage", out var metadata)
            && metadata.GetValueOrDefault("uniqueKeys") is IEnumerable<IEnumerable<string>> keys)
        {
            foreach (var (key, index) in keys.Select((key, index) => (key, index)))
                yield return KeyPlan(inputs, key.ToArray(), $"{prefix}:uniqueKey:{index}");
        }
    }

    private IEnumerable<IExecutableRule> ForeignPlans(string assetId, DataSchema schema, ConstraintInput[] inputs, string prefix)
    {
        for (var index = 0; index < schema.Relationships.Count; index++)
        {
            var relationship = schema.Relationships[index];
            var prerequisite = _relationships.FirstOrDefault(check => check.AssetId == assetId && check.RelationshipIndex == index);
            var targets = relationship.TargetAsset == assetId ? inputs : Inputs(relationship.TargetAsset);
            var blocked = prerequisite is null ? "RELATIONSHIP_PREREQUISITE_UNAVAILABLE" : prerequisite.DiagnosticCode;
            yield return Plan(inputs[0], $"{prefix}:foreignKey:{index}", relationship.Fields, QualityInputScope.Dataset,
                (execution, token) => blocked is null
                    ? ConstraintKeyMeasurement.ForeignAsync(inputs, relationship.Fields, targets, relationship.TargetFields, execution, token)
                    : ValueTask.FromResult(Incomplete(blocked)));
        }
    }

    private IEnumerable<IExecutableRule> FieldPlans(DataSchema schema, ConstraintInput input, DataField field, string prefix)
    {
        var parser = new ConstraintValueParser(schema, field);
        var type = field.LogicalType ?? "any";
        var expectedType = field.Format is null ? $"logical type {type}" : $"logical type {type} ({field.Format})";
        yield return CellPlan(input, field, $"{prefix}:logicalType", expectedType,
            parser.Supported ? cell => cell.State == ConstraintCellState.Invalid : null, "LOGICAL_TYPE_OR_FORMAT_UNSUPPORTED");

        if (!field.Nullable)
            yield return CellPlan(input, field, $"{prefix}:required", "non-null value", cell => cell.State is ConstraintCellState.Invalid or ConstraintCellState.Missing);

        foreach (var plan in DeclaredCellPlans(input, field, prefix, parser))
            yield return plan;

        if (field.ValueDomain is { Reference: { } reference } domain
            && (domain.AllowedValues is null || reference.EffectiveEncoding == ValueEncoding.Label))
        {
            yield return CategoryPlan(input, field, $"{prefix}:category", domain, reference, parser.Supported);
        }
    }

    private static IEnumerable<IExecutableRule> DeclaredCellPlans(ConstraintInput input, DataField field, string prefix, ConstraintValueParser parser)
    {
        var constraintIndex = 0;
        foreach (var constraint in field.Constraints)
        {
            var identity = $"{prefix}:{constraint.Kind}:{constraintIndex++}";
            if (constraint.Kind == "primaryKey" || constraint is { Kind: "unique", Value: bool }) continue;
            if (constraint.Kind == "enum" && field.ValueDomain is { Reference.EffectiveEncoding: ValueEncoding.Label }) continue;

            var predicate = Predicate(constraint, parser);
            yield return CellPlan(input, field, identity, predicate.Expected, predicate.Violates, predicate.Diagnostic);
        }
    }

    private IExecutableRule CategoryPlan(ConstraintInput input, DataField field, string id, ValueDomain domain,
        ValueDomainReference reference, bool supported)
        => Plan(input, id, [field.Name], QualityInputScope.Dataset, (execution, token) =>
            supported
                ? ConstraintKeyMeasurement.CategoryAsync(input, field.Name, Inputs(reference.BoundAssetId ?? ""), domain, execution, token)
                : ValueTask.FromResult(Incomplete("LOGICAL_TYPE_OR_FORMAT_UNSUPPORTED")));

    private static CellPredicate Predicate(DataConstraint constraint, ConstraintValueParser parser)
    {
        if (!parser.Supported) return new(Diagnostic: "LOGICAL_TYPE_OR_FORMAT_UNSUPPORTED");
        try
        {
            return constraint.Kind switch
            {
                "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum" => BoundPredicate(constraint, parser),
                "minLength" or "maxLength" when parser.HasTextValues => LengthPredicate(constraint),
                "pattern" when parser.HasTextValues && constraint.Value is string pattern => PatternPredicate(pattern),
                "enum" when constraint.Value is IEnumerable values && constraint.Value is not string => EnumPredicate(values),
                _ => new()
            };
        }
        catch (ArgumentException)
        {
            return new(Diagnostic: "CONSTRAINT_OPERAND_INVALID");
        }
    }

    private static CellPredicate BoundPredicate(DataConstraint constraint, ConstraintValueParser parser)
    {
        if (parser.Bound(constraint.Value) is not { } bound) return new();
        var op = constraint.Kind switch
        {
            "minimum" => ExpectationOperator.GreaterOrEqual,
            "maximum" => ExpectationOperator.LessOrEqual,
            "exclusiveMinimum" => ExpectationOperator.Greater,
            _ => ExpectationOperator.Less
        };
        var expectation = new QualityExpectation(op, bound);
        return new(cell => cell.Value is { } value && !ExpectationAssertionPlan.Matches(value, expectation), bound.ToObject());
    }

    private static CellPredicate LengthPredicate(DataConstraint constraint)
    {
        if (!ScalarValue.TryCreate(constraint.Value, out var length)
            || length is not { Kind: ScalarKind.Integer, Value: long size } || size < 0)
            return new();

        return new(cell => cell.Value?.ToObject() is string text && (constraint.Kind == "minLength"
            ? Length(text) < size : Length(text) > size), size);
    }

    private static CellPredicate PatternPredicate(string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return new(cell => cell.Value?.ToObject() is string text && !regex.IsMatch(text), pattern);
    }

    private static CellPredicate EnumPredicate(IEnumerable values)
    {
        var allowed = values.Cast<object?>().Select(ScalarValue.From).ToHashSet();
        return new(cell => cell.State == ConstraintCellState.Value && (cell.Scalar is not { } scalar || !allowed.Contains(scalar)),
            "effective allowed values");
    }

    private sealed record CellPredicate(Func<ConstraintCell, bool>? Violates = null, object? Expected = null,
        string Diagnostic = "CONSTRAINT_UNSUPPORTED");

    private static int Length(string text)
    {
        var length = 0;
        foreach (var rune in text.EnumerateRunes()) length++;
        return length;
    }

    private static IExecutableRule CellPlan(ConstraintInput input, DataField field, string id, object? expected,
        Func<ConstraintCell, bool>? violates, string diagnostic = "CONSTRAINT_UNSUPPORTED")
        => Plan(input, id, [field.Name], QualityInputScope.Row, async (context, token) =>
        {
            if (violates is null) return Incomplete(diagnostic);
            var scan = await ScanAsync(input, [field.Name], context, token).ConfigureAwait(false);
            var ordinal = scan.Ordinal(field.Name);
            var tally = new ConstraintTally(context.Limits, expected);
            for (var index = 0; index < scan.Rows.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var cell = scan.Rows[index][ordinal];
                try
                {
                    if (violates!(cell)) tally.Record(input.Source, index, [cell.Raw]);
                }
                catch (RegexMatchTimeoutException) { return tally.Measure(index, "REGEX_BUDGET"); }
            }

            return tally.Measure(scan.Rows.Count, scan.DiagnosticCode);
        });

    private static IExecutableRule KeyPlan(ConstraintInput[] inputs, IReadOnlyList<string> fields, string id, bool primaryKey = false)
        => Plan(inputs[0], id, fields, QualityInputScope.Dataset,
            (context, token) => ConstraintKeyMeasurement.UniqueAsync(inputs, fields, primaryKey, context, token));

    private static IExecutableRule Plan(ConstraintInput input, string id, IReadOnlyList<string> fields, QualityInputScope scope,
        Func<RuleExecutionContext, CancellationToken, ValueTask<RuleMeasurement>> measure)
        => new MeasuredExecutableRule(new(id, null, input.Source, RuleCategory.Constraint, new(input.Asset.Id, fields),
            "packata.constraint", "1", input.EndpointId), new(scope), new Measurement(measure),
            new ExpectationAssertionPlan([new(ExpectationOperator.Equal, QualityValue.From(0L))]));

    internal static ValueTask<ConstraintScan> ScanAsync(ConstraintInput input, IReadOnlyList<string> fields, RuleExecutionContext context, CancellationToken token)
    {
        if (input.ReadError) throw new IOException("The input could not be inspected.");
        if (input.DiagnosticCode is { } diagnostic) throw new IncompleteRuleInputException(diagnostic);
        if (fields.Any(field => input.AmbiguousFields?.Contains(field) == true))
            throw new IncompleteRuleInputException("CONSTRAINT_COLUMN_AMBIGUOUS");
        if (fields.Any(field => !input.Columns.ContainsKey(field)))
            throw new IncompleteRuleInputException("CONSTRAINT_COLUMN_UNAVAILABLE");
        if (context.ConstraintInput is null) throw new IncompleteRuleInputException("CONSTRAINT_INPUT_CONTEXT_REQUIRED");
        return context.ConstraintInput.ScanAsync(input, context.Limits, token);
    }

    internal static RuleMeasurement Incomplete(string code)
        => new(null, Complete: false, Diagnostics: [new(code, "The constraint could not be evaluated with the available declaration or input.")]);

    private sealed class Measurement(Func<RuleExecutionContext, CancellationToken, ValueTask<RuleMeasurement>> measure) : IMeasurementPlan
    {
        public ValueTask<RuleMeasurement> MeasureAsync(RuleExecutionContext context, CancellationToken cancellationToken)
            => measure(context, cancellationToken);
    }
}

internal sealed class ConstraintTally(RuleLimits limits, object? expected)
{
    private readonly List<RuleEvidence> _evidence = [];
    private long _violations;

    public void Record(string source, int rowIndex, IEnumerable<object?> observed)
    {
        _violations++;
        if (_evidence.Count >= limits.MaxEvidence) return;
        _evidence.Add(new("violation", QualityValue.From(new Dictionary<string, object?>
        {
            ["file"] = source,
            ["record"] = rowIndex + 1L,
            ["observed"] = observed.Select(EvidenceValue).ToArray(),
            ["expected"] = EvidenceValue(expected)
        })));
    }

    public RuleMeasurement Measure(long population, string? diagnostic = null)
        => new(QualityValue.From(_violations), "rows", population, diagnostic is null, _evidence.AsReadOnly(),
            diagnostic is null ? [] : [new(diagnostic, "The measurement is incomplete. Counts cover only inspected rows.")]);

    private static object? EvidenceValue(object? value) => value switch
    {
        string text when text.Length > 160 => text[..160],
        null or DBNull => null,
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        _ when ScalarValue.TryCreate(value, out var scalar) => scalar.Value,
        _ => value.GetType().Name
    };
}
