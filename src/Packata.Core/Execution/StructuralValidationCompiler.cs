using Packata.Core.Contracts;

namespace Packata.Core.Execution;

/// <summary>Observed column identity and authoritative logical type, when available. No inferred value guarantees.</summary>
public sealed record ObservedColumn(string Name, string? LogicalType = null);

/// <summary>One input's unprojected structure. Null columns mean unavailable evidence, not an empty schema.</summary>
public sealed record DatasetStructure(string AssetId, string Source, IReadOnlyList<ObservedColumn>? Columns,
    string? EndpointId = null, bool TypesAvailable = false, string? DiagnosticCode = null, bool ReadError = false);

/// <summary>Compiles structural comparisons over supplied observations; never opens data or parses descriptors.</summary>
public sealed class StructuralValidationCompiler : IRuleCompiler<DataAsset>
{
    private readonly DatasetStructure[] _observations;

    public StructuralValidationCompiler(IEnumerable<DatasetStructure> observations)
        => _observations = observations.Select(o => o with
        { Columns = o.Columns is null ? null : Array.AsReadOnly(o.Columns.ToArray()) }).ToArray();

    public CompilationResult Compile(DataAsset asset, RuleCompilationContext context)
    {
        var plans = new List<IExecutableRule>();
        var findings = new List<CompilationFinding>();
        if (asset.Schema is not { } schema)
            return new([], [new($"structure:{asset.Id}", CompilationFindingKind.Invalid, "SCHEMA_MISSING", "An explicit schema is required for structural validation.")]);
        var fields = schema.Fields;
        var names = fields.Select(f => f.PhysicalName ?? f.Name).ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length
            || !Enum.IsDefined(schema.PropertyMatching) || !Enum.IsDefined(schema.PropertyOrder))
            return new([], [new($"structure:{asset.Id}", CompilationFindingKind.Invalid, "SCHEMA_AMBIGUOUS", "Property identities and matching/order settings must be valid and unambiguous.")]);
        var inputs = _observations.Where(o => o.AssetId == asset.Id && (context.EndpointId is null || context.EndpointId == o.EndpointId)).ToArray();
        if (inputs.Length == 0) inputs = [new(asset.Id, asset.Id, null, context.EndpointId, DiagnosticCode: "STRUCTURE_UNAVAILABLE")];
        for (var inputIndex = 0; inputIndex < inputs.Length; inputIndex++)
        {
            var input = inputs[inputIndex];
            var prefix = $"structure:{asset.Id}:{inputIndex}";
            Add("propertyMatching", null, () =>
            {
                if (Unavailable(input) is { } missing) return missing;
                var actual = input.Columns!.Select(c => c.Name).ToArray();
                var expectedSet = names.ToHashSet(StringComparer.Ordinal);
                var actualSet = actual.ToHashSet(StringComparer.Ordinal);
                var matches = actual.Length == actualSet.Count && actual.All(n => !string.IsNullOrWhiteSpace(n)) && schema.PropertyMatching switch
                {
                    PropertyMatching.Equal => expectedSet.SetEquals(actualSet),
                    PropertyMatching.Subset => expectedSet.IsSubsetOf(actualSet),
                    PropertyMatching.Superset => actualSet.IsSubsetOf(expectedSet),
                    PropertyMatching.Partial => expectedSet.Overlaps(actualSet),
                    _ => false
                };
                return Boolean(matches, input.Columns!.Count,
                    new("expected", QualityValue.From(names)), new("actual", QualityValue.From(actual)),
                    new("missing", QualityValue.From(names.Except(actual, StringComparer.Ordinal).ToArray())),
                    new("additional", QualityValue.From(actual.Except(names, StringComparer.Ordinal).ToArray())));
            });
            if (schema.PropertyOrder == PropertyOrder.Declared)
                Add("propertyOrder", null, () =>
                {
                    if (Unavailable(input) is { } missing) return missing;
                    var actual = input.Columns!.Select(c => c.Name).ToArray();
                    if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length)
                        return Unknown("AMBIGUOUS_COLUMNS");
                    var expected = names.Where(n => actual.Contains(n, StringComparer.Ordinal)).ToArray();
                    var present = actual.Where(n => names.Contains(n, StringComparer.Ordinal)).ToArray();
                    return Boolean(expected.SequenceEqual(present, StringComparer.Ordinal), actual.Length,
                        new("expected", QualityValue.From(expected)), new("actual", QualityValue.From(present)));
                });
            if (input.TypesAvailable)
                foreach (var field in fields.Where(f => input.Columns is null || input.Columns.Any(c => c.Name == (f.PhysicalName ?? f.Name))))
                    Add("schemaType", field.Name, () =>
                    {
                        if (Unavailable(input) is { } missing) return missing;
                        var columns = input.Columns!.Where(c => c.Name == (field.PhysicalName ?? field.Name)).ToArray();
                        if (columns.Length != 1) return Unknown("TYPE_COLUMN_UNAVAILABLE");
                        var actual = columns[0].LogicalType;
                        if (!KnownType(actual) || !KnownType(field.LogicalType)) return Unknown("TYPE_UNSUPPORTED");
                        return Boolean(TypeFits(actual, field.LogicalType), 1,
                            new("expected", QualityValue.From(field.LogicalType)), new("actual", QualityValue.From(actual)));
                    });
            for (var relationshipIndex = 0; relationshipIndex < schema.Relationships.Count; relationshipIndex++)
            {
                var relationship = schema.Relationships[relationshipIndex];
                Add($"relationship:{relationshipIndex}", null, () => Relationship(asset, input, relationship, context.Contract));
            }

            void Add(string kind, string? field, Func<RuleMeasurement> measure)
            {
                var descriptor = new RuleDescriptor($"{prefix}:{kind}:{field}", null, input.Source,
                    RuleCategory.Constraint, new(asset.Id, field is null ? [] : [field]), "packata.structure", "1", input.EndpointId);
                plans.Add(new MeasuredExecutableRule(descriptor, new(QualityInputScope.Observation),
                    new StructuralMeasurement(measure), new ExpectationAssertionPlan([new(ExpectationOperator.Equal, QualityValue.From(true))])));
            }
        }
        return new(Array.AsReadOnly(plans.ToArray()), Array.AsReadOnly(findings.ToArray()));
    }

    private RuleMeasurement Relationship(DataAsset sourceAsset, DatasetStructure source, DataRelationship relationship, DataContract? contract)
    {
        if (Unavailable(source) is { } missing) return missing;
        if (!relationship.Kind.Equals("foreignKey", StringComparison.OrdinalIgnoreCase)) return Unknown("RELATIONSHIP_UNSUPPORTED");
        var targets = contract?.Assets.Where(a => a.Id == relationship.TargetAsset).ToArray() ?? [];
        if (targets.Length != 1 || targets[0].Schema is null || relationship.Fields.Count == 0
            || relationship.Fields.Count != relationship.TargetFields.Count) return Unknown("RELATIONSHIP_DECLARATION_UNRESOLVED");
        var targetAsset = targets[0];
        var observations = relationship.TargetAsset == source.AssetId ? [source] : _observations.Where(o => o.AssetId == relationship.TargetAsset).ToArray();
        if (observations.Length == 0) return Unknown("RELATIONSHIP_TARGET_UNAVAILABLE");
        foreach (var target in observations)
        {
            if (Unavailable(target) is { } unavailable) return unavailable;
            for (var index = 0; index < relationship.Fields.Count; index++)
            {
                var from = sourceAsset.Schema!.Fields.Where(f => f.Name == relationship.Fields[index]).ToArray();
                var to = targetAsset.Schema!.Fields.Where(f => f.Name == relationship.TargetFields[index]).ToArray();
                if (from.Length != 1 || to.Length != 1) return Unknown("RELATIONSHIP_DECLARATION_UNRESOLVED");
                var sourceColumns = source.Columns!.Where(c => c.Name == (from[0].PhysicalName ?? from[0].Name)).ToArray();
                var targetColumns = target.Columns!.Where(c => c.Name == (to[0].PhysicalName ?? to[0].Name)).ToArray();
                if (sourceColumns.Length != 1 || targetColumns.Length != 1)
                    return Boolean(false, 1, new("sourceProperty", QualityValue.From(from[0].Name)), new("targetProperty", QualityValue.From(to[0].Name)),
                        new("targetInput", QualityValue.From(target.Source)));
                var sourceType = source.TypesAvailable ? sourceColumns[0].LogicalType : from[0].LogicalType;
                var targetType = target.TypesAvailable ? targetColumns[0].LogicalType : to[0].LogicalType;
                if (!KnownType(sourceType) || !KnownType(targetType)) return Unknown("RELATIONSHIP_TYPE_UNSUPPORTED");
                if (!TypeFits(sourceType, targetType)) return Boolean(false, 1,
                    new("sourceType", QualityValue.From(sourceType)), new("targetType", QualityValue.From(targetType)));
            }
        }
        return Boolean(true, observations.Length);
    }

    private static bool KnownType(string? type) => type is "string" or "integer" or "number" or "boolean" or "date" or "time" or "timestamp" or "datetime" or "array" or "object";
    private static bool TypeFits(string? offered, string? accepted) => offered == accepted || offered == "integer" && accepted == "number"
        || offered is "datetime" or "timestamp" && accepted is "datetime" or "timestamp";
    private static RuleMeasurement? Unavailable(DatasetStructure input)
    {
        if (input.ReadError) throw new IOException("Dataset structure could not be read.");
        return input.Columns is null || input.DiagnosticCode is not null ? Unknown(input.DiagnosticCode ?? "STRUCTURE_UNAVAILABLE") : null;
    }
    private static RuleMeasurement Unknown(string code) => new(null, Complete: false,
        Diagnostics: [new(code, "Structural evidence or a supported declaration is required; no complete result is available.")]);
    private static RuleMeasurement Boolean(bool value, long population, params RuleEvidence[] evidence)
        => new(QualityValue.From(value), Population: population, Evidence: Array.AsReadOnly(evidence));

    private sealed class StructuralMeasurement(Func<RuleMeasurement> measure) : IMeasurementPlan
    {
        public ValueTask<RuleMeasurement> MeasureAsync(RuleExecutionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(measure());
        }
    }
}
