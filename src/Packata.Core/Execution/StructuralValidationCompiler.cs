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
        if (asset.Schema is not { } schema)
            return Invalid(asset, "SCHEMA_MISSING", "An explicit schema is required for structural validation.");
        if (!ValidSchema(schema))
            return Invalid(asset, "SCHEMA_AMBIGUOUS", "Property identities and matching/order settings must be valid and unambiguous.");
        var inputs = _observations.Where(o => o.AssetId == asset.Id && (context.EndpointId is null || context.EndpointId == o.EndpointId)).ToArray();
        if (inputs.Length == 0) inputs = [new(asset.Id, asset.Id, null, context.EndpointId, DiagnosticCode: "STRUCTURE_UNAVAILABLE")];
        var plans = inputs.SelectMany((input, index) => InputPlans(asset, schema, input, index, context)).ToArray();
        return new(Array.AsReadOnly(plans), []);
    }

    private static CompilationResult Invalid(DataAsset asset, string code, string message)
        => new([], [new($"structure:{asset.Id}", CompilationFindingKind.Invalid, code, message)]);

    private static bool ValidSchema(DataSchema schema)
    {
        var names = schema.Fields.Select(PhysicalName).ToArray();
        return !names.Any(string.IsNullOrWhiteSpace) && names.Distinct(StringComparer.Ordinal).Count() == names.Length
            && Enum.IsDefined(schema.PropertyMatching) && Enum.IsDefined(schema.PropertyOrder);
    }

    private IEnumerable<IExecutableRule> InputPlans(DataAsset asset, DataSchema schema, DatasetStructure input, int index, RuleCompilationContext context)
    {
        var names = schema.Fields.Select(PhysicalName).ToArray();
        var prefix = $"structure:{asset.Id}:{index}";
        yield return Plan(asset, input, prefix, "propertyMatching", null, () => MatchProperties(schema.PropertyMatching, names, input));
        if (schema.PropertyOrder == PropertyOrder.Declared)
            yield return Plan(asset, input, prefix, "propertyOrder", null, () => MatchOrder(names, input));
        if (input.TypesAvailable)
            foreach (var field in schema.Fields.Where(f => IsPresent(input, f)))
                yield return Plan(asset, input, prefix, "schemaType", field.Name, () => MatchType(field, input));
        for (var relationshipIndex = 0; relationshipIndex < schema.Relationships.Count; relationshipIndex++)
        {
            var relationship = schema.Relationships[relationshipIndex];
            yield return Plan(asset, input, prefix, $"relationship:{relationshipIndex}", null,
                () => Relationship(asset, input, relationship, context.Contract));
        }
    }

    private static IExecutableRule Plan(DataAsset asset, DatasetStructure input, string prefix, string kind, string? field, Func<RuleMeasurement> measure)
    {
        var descriptor = new RuleDescriptor($"{prefix}:{kind}:{field}", null, input.Source,
            RuleCategory.Constraint, new(asset.Id, field is null ? [] : [field]), "packata.structure", "1", input.EndpointId);
        return new MeasuredExecutableRule(descriptor, new(QualityInputScope.Observation),
            new StructuralMeasurement(measure), new ExpectationAssertionPlan([new(ExpectationOperator.Equal, QualityValue.From(true))]));
    }

    private static bool IsPresent(DatasetStructure input, DataField field)
        => input.Columns is null || input.Columns.Any(c => c.Name == PhysicalName(field));
    private static string PhysicalName(DataField field) => field.PhysicalName ?? field.Name;

    private static RuleMeasurement MatchProperties(PropertyMatching matching, string[] names, DatasetStructure input)
    {
        if (Unavailable(input) is { } missing) return missing;
        var actual = input.Columns!.Select(c => c.Name).ToArray();
        var expectedSet = names.ToHashSet(StringComparer.Ordinal);
        var actualSet = actual.ToHashSet(StringComparer.Ordinal);
        var validNames = actual.Length == actualSet.Count && actual.All(n => !string.IsNullOrWhiteSpace(n));
        var matches = SetMatches(matching, expectedSet, actualSet);
        return Boolean(validNames && matches, actual.Length,
            new("expected", QualityValue.From(names)), new("actual", QualityValue.From(actual)),
            new("missing", QualityValue.From(names.Except(actual, StringComparer.Ordinal).ToArray())),
            new("additional", QualityValue.From(actual.Except(names, StringComparer.Ordinal).ToArray())));
    }

    private static bool SetMatches(PropertyMatching matching, HashSet<string> expected, HashSet<string> actual) => matching switch
    {
        PropertyMatching.Equal => expected.SetEquals(actual),
        PropertyMatching.Subset => expected.IsSubsetOf(actual),
        PropertyMatching.Superset => actual.IsSubsetOf(expected),
        PropertyMatching.Partial => expected.Overlaps(actual),
        _ => false
    };

    private static RuleMeasurement MatchOrder(string[] names, DatasetStructure input)
    {
        if (Unavailable(input) is { } missing) return missing;
        var actual = input.Columns!.Select(c => c.Name).ToArray();
        if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length) return Unknown("AMBIGUOUS_COLUMNS");
        var expected = names.Where(n => actual.Contains(n, StringComparer.Ordinal)).ToArray();
        var present = actual.Where(n => names.Contains(n, StringComparer.Ordinal)).ToArray();
        return Boolean(expected.SequenceEqual(present, StringComparer.Ordinal), actual.Length,
            new("expected", QualityValue.From(expected)), new("actual", QualityValue.From(present)));
    }

    private static RuleMeasurement MatchType(DataField field, DatasetStructure input)
    {
        if (Unavailable(input) is { } missing) return missing;
        var columns = input.Columns!.Where(c => c.Name == PhysicalName(field)).ToArray();
        if (columns.Length != 1) return Unknown("TYPE_COLUMN_UNAVAILABLE");
        var actual = columns[0].LogicalType;
        if (!KnownType(actual) || !KnownType(field.LogicalType)) return Unknown("TYPE_UNSUPPORTED");
        return Boolean(TypeFits(actual, field.LogicalType), 1,
            new("expected", QualityValue.From(field.LogicalType)), new("actual", QualityValue.From(actual)));
    }

    private RuleMeasurement Relationship(DataAsset sourceAsset, DatasetStructure source, DataRelationship relationship, DataContract? contract)
    {
        if (Unavailable(source) is { } missing) return missing;
        if (!relationship.Kind.Equals("foreignKey", StringComparison.OrdinalIgnoreCase)) return Unknown("RELATIONSHIP_UNSUPPORTED");
        var targetAsset = RelationshipTarget(relationship, contract);
        if (targetAsset is null) return Unknown("RELATIONSHIP_DECLARATION_UNRESOLVED");
        var observations = relationship.TargetAsset == source.AssetId ? [source] : _observations.Where(o => o.AssetId == relationship.TargetAsset).ToArray();
        if (observations.Length == 0) return Unknown("RELATIONSHIP_TARGET_UNAVAILABLE");
        foreach (var target in observations)
        {
            if (Unavailable(target) is { } unavailable) return unavailable;
            if (CompareKeys(sourceAsset, targetAsset, source, target, relationship) is { } comparison) return comparison;
        }
        return Boolean(true, observations.Length);
    }

    private static DataAsset? RelationshipTarget(DataRelationship relationship, DataContract? contract)
    {
        var targets = contract?.Assets.Where(a => a.Id == relationship.TargetAsset).ToArray() ?? [];
        if (targets.Length != 1 || targets[0].Schema is null || relationship.Fields.Count == 0
            || relationship.Fields.Count != relationship.TargetFields.Count) return null;
        return targets[0];
    }

    private static RuleMeasurement? CompareKeys(DataAsset fromAsset, DataAsset toAsset, DatasetStructure source,
        DatasetStructure target, DataRelationship relationship)
    {
        for (var index = 0; index < relationship.Fields.Count; index++)
            if (CompareKey(fromAsset, toAsset, source, target, relationship.Fields[index], relationship.TargetFields[index]) is { } comparison)
                return comparison;
        return null;
    }

    private static RuleMeasurement? CompareKey(DataAsset fromAsset, DataAsset toAsset, DatasetStructure source,
        DatasetStructure target, string sourceName, string targetName)
    {
        var from = fromAsset.Schema!.Fields.Where(f => f.Name == sourceName).ToArray();
        var to = toAsset.Schema!.Fields.Where(f => f.Name == targetName).ToArray();
        if (from.Length != 1 || to.Length != 1) return Unknown("RELATIONSHIP_DECLARATION_UNRESOLVED");
        var sourceColumns = source.Columns!.Where(c => c.Name == PhysicalName(from[0])).ToArray();
        var targetColumns = target.Columns!.Where(c => c.Name == PhysicalName(to[0])).ToArray();
        if (sourceColumns.Length != 1 || targetColumns.Length != 1)
            return Boolean(false, 1, new("sourceProperty", QualityValue.From(from[0].Name)), new("targetProperty", QualityValue.From(to[0].Name)),
                new("targetInput", QualityValue.From(target.Source)));
        var sourceType = source.TypesAvailable ? sourceColumns[0].LogicalType : from[0].LogicalType;
        var targetType = target.TypesAvailable ? targetColumns[0].LogicalType : to[0].LogicalType;
        if (!KnownType(sourceType) || !KnownType(targetType)) return Unknown("RELATIONSHIP_TYPE_UNSUPPORTED");
        if (!TypeFits(sourceType, targetType))
            return Boolean(false, 1, new("sourceType", QualityValue.From(sourceType)), new("targetType", QualityValue.From(targetType)));
        return null;
    }

    private static bool KnownType(string? type) => type is "string" or "integer" or "number" or "boolean" or "date" or "time" or "timestamp" or "datetime" or "array" or "object";
    private static bool TypeFits(string? offered, string? accepted) => (offered, accepted) switch
    {
        ("integer", "number") => true,
        ("datetime" or "timestamp", "datetime" or "timestamp") => true,
        _ => offered == accepted
    };
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
