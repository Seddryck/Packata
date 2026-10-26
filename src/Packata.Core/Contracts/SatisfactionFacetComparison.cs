namespace Packata.Core.Contracts;

internal sealed class SatisfactionFacetComparison(
    Action<string, SatisfactionStatus, ContractPath, object?, object?, string> add)
{
    internal void Compare(DataField? offered, DataField accepted, ContractPath path, SatisfactionStatus type)
    {
        var producerConstraints = offered?.Constraints ?? [];
        var applicable = accepted.LogicalType == "string" ? new[] { "maxLength" } : [];
        var kinds = accepted.Constraints.Select(x => x.Kind).Union(producerConstraints.Select(x => x.Kind), StringComparer.Ordinal)
            .Union(applicable, StringComparer.Ordinal)
            .Except(["precision", "scale"], StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (var kind in kinds)
        {
            var left = Value(accepted, kind);
            var right = offered is null ? null : Value(offered, kind);
            var status = type == SatisfactionStatus.Satisfied && offered is not null
                ? Constraint(kind, accepted, offered) : SatisfactionStatus.Unknown;
            add("SAT120:" + kind, status, path.Append("constraints").Append(kind), right, left,
                status == SatisfactionStatus.Unknown ? "Constraint containment is unsupported or blocked by field/type resolution."
                    : "Producer-permitted values must fit the consumer-accepted constraint; an omitted native bound is unbounded.");
        }
        CompareDecimal(offered, accepted, path, type);
    }

    private static SatisfactionStatus Constraint(string kind, DataField accepted, DataField offered)
    {
        if (Value(accepted, kind) is null && Value(offered, kind) is null) return SatisfactionStatus.Satisfied;
        var result = kind switch
        {
            "minLength" or "minItems" or "minProperties" => CompatibilityRules.CompareConstraint(kind,
                Value(accepted, kind) ?? 0, Value(offered, kind) ?? 0),
            "minimum" or "exclusiveMinimum" => CompatibilityRules.CompareBounds(accepted.Constraints, offered.Constraints, true),
            "maximum" or "exclusiveMaximum" => CompatibilityRules.CompareBounds(accepted.Constraints, offered.Constraints, false),
            _ => CompatibilityRules.CompareConstraint(kind, Value(accepted, kind), Value(offered, kind))
        };
        return SatisfactionAnalysis.Convert(result);
    }

    private void CompareDecimal(DataField? offered, DataField accepted, ContractPath path, SatisfactionStatus type)
    {
        if (!HasDecimalParameters(accepted) && (offered is null || !HasDecimalParameters(offered))) return;
        var prerequisite = type == SatisfactionStatus.Satisfied && accepted.LogicalType == "number" && offered?.LogicalType == "number";
        CompareDecimalParameter("scale", Integer(offered, "scale"), Integer(accepted, "scale"), prerequisite, path);
        var producerCapacity = Integer(offered, "precision") - Integer(offered, "scale");
        var consumerCapacity = Integer(accepted, "precision") - Integer(accepted, "scale");
        CompareDecimalParameter("precision", producerCapacity, consumerCapacity, prerequisite, path);
    }

    private void CompareDecimalParameter(string parameter, int? offered, int? accepted, bool prerequisite, ContractPath path)
    {
        var status = prerequisite ? SatisfactionAnalysis.Convert(CompatibilityRules.CompareCapacity(accepted, offered))
            : SatisfactionStatus.Unknown;
        add("SAT121:" + parameter, status, path.Append("constraints").Append(parameter), offered, accepted,
            parameter == "precision" ? "Fixed-decimal integral capacity (precision minus scale) must fit; incomplete parameters remain unresolved."
                : "Fixed-decimal fractional capacity must fit; incomplete parameters remain unresolved.");
    }

    private static bool HasDecimalParameters(DataField field) => field.Constraints.Any(x => x.Kind is "precision" or "scale");
    private static object? Value(DataField field, string kind) => field.Constraints.SingleOrDefault(x => x.Kind == kind)?.Value;
    private static int? Integer(DataField? field, string kind) => field is null ? null : Value(field, kind) as int?;
}
