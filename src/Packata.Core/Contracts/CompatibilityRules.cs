using System.Collections;
using System.Globalization;

namespace Packata.Core.Contracts;

internal static class CompatibilityRules
{
    internal static bool KnownType(string? type) => type is "string" or "integer" or "number" or "boolean"
        or "date" or "time" or "timestamp" or "datetime" or "object" or "array";
    internal static bool KnownConstraint(string kind) => kind is "minimum" or "maximum" or "exclusiveMinimum"
        or "exclusiveMaximum" or "minLength" or "maxLength" or "minItems" or "maxItems" or "minProperties"
        or "maxProperties" or "unique" or "uniqueItems" or "primaryKey" or "enum" or "pattern" or "required";

    internal static bool SupportedConstraintValue(DataConstraint constraint) => constraint.Kind switch
    {
        "unique" or "uniqueItems" or "primaryKey" => constraint.Value is bool,
        "enum" or "required" => constraint.Value is IEnumerable and not string,
        "pattern" => constraint.Value is string,
        "minLength" or "maxLength" or "minItems" or "maxItems" or "minProperties" or "maxProperties"
            => Number(constraint.Value, out var count) && count >= 0 && count == decimal.Truncate(count),
        _ => KnownConstraint(constraint.Kind) && Number(constraint.Value, out _)
    };

    internal static IEnumerable<CompatibilityFinding> Classify(IContractChange change)
    {
        var findings = new List<CompatibilityFinding>();
        switch (change)
        {
            case DatasetAdded:
                Add("dataset-added", CompatibilityClassification.NonBreaking, "Additional datasets do not affect existing name-based consumers."); break;
            case DatasetRemoved:
                Add("dataset-removed", CompatibilityClassification.Breaking, "A dataset available to baseline consumers was removed."); break;
            case PropertyAdded when IsItems(change.Path):
            case PropertyRemoved when IsItems(change.Path):
                Add("items-shape", CompatibilityClassification.Unknown, "Array item definition changes require semantics not covered by this policy."); break;
            case PropertyAdded:
                Add("property-added", CompatibilityClassification.NonBreaking, "Additional fields are allowed by the policy's open-record assumption."); break;
            case PropertyRemoved:
                Add("property-removed", CompatibilityClassification.Breaking, "A field available to baseline consumers was removed."); break;
            case PropertyTypeChanged type:
                Add("logical-type", Type(type.Previous, type.Current), TypeExplanation(type.Previous, type.Current)); break;
            case PropertyModified property:
                Field(property.Previous, property.Current); break;
            case DatasetModified dataset:
                if (dataset.Previous.Name != dataset.Current.Name || dataset.Previous.PhysicalName != dataset.Current.PhysicalName
                    || dataset.Previous.Kind != dataset.Current.Kind || !Equal(dataset.Previous.EndpointBindings, dataset.Current.EndpointBindings))
                    Add("dataset-access", CompatibilityClassification.PotentiallyBreaking, "Dataset naming, representation, or endpoint bindings changed; consumer access assumptions may be affected.");
                if (!Equal(dataset.Previous.Schema?.PrimaryKey, dataset.Current.Schema?.PrimaryKey)
                    || !Equal(dataset.Previous.Schema?.Extensions, dataset.Current.Schema?.Extensions)
                    || !Equal(dataset.Previous.Extensions, dataset.Current.Extensions)
                    || (dataset.Previous.Schema is null) != (dataset.Current.Schema is null))
                    Add("dataset-semantics", CompatibilityClassification.Unknown, "Schema keys, schema availability, or extension semantics require additional policy rules.");
                break;
            case ContractMetadataChanged metadata:
                if (metadata.Path.Value == "metadata") break;
                if (metadata.Previous is ContractIdentity previous && metadata.Current is ContractIdentity current)
                {
                    if (previous.Id != current.Id || previous.Name != current.Name)
                        Add("contract-identity", CompatibilityClassification.PotentiallyBreaking, "Contract identity changed and may affect consumer discovery.");
                }
                else Add("contract-metadata", CompatibilityClassification.Unknown, "Governance or extension metadata may carry semantics this policy cannot classify.");
                break;
            case IServerChange:
                Add("server-access", CompatibilityClassification.PotentiallyBreaking, "Server access or representation changed; operational impact depends on the consumer."); break;
            default:
                Add("unsupported-change", CompatibilityClassification.Unknown, "Relationship, quality, SLA, or unrecognized change semantics have no supported rule."); break;
        }
        if (findings.Count == 0)
            Add("descriptive-metadata", CompatibilityClassification.NonBreaking, "Only descriptive metadata changed; producer structural guarantees are unchanged.");
        return findings;

        void Add(string rule, CompatibilityClassification classification, string explanation, string? facet = null, string? key = null)
        {
            var path = facet is null ? change.Path : change.Path.Append(facet);
            findings.Add(new(key is null ? path : path.Append(key), classification, rule, explanation, change));
        }

        void Field(DataField previous, DataField current)
        {
            if (previous.Required != current.Required)
                Add("requiredness", current.Required ? CompatibilityClassification.NonBreaking : CompatibilityClassification.Breaking,
                    current.Required ? "The candidate guarantees field presence." : "The candidate no longer guarantees field presence.", "required");
            if (previous.Nullable != current.Nullable)
                Add("nullability", current.Nullable ? CompatibilityClassification.Breaking : CompatibilityClassification.NonBreaking,
                    current.Nullable ? "Candidate data may contain nulls that baseline consumers did not accept." : "The candidate strengthens the non-null guarantee.", "nullable");
            if (previous.Format != current.Format || previous.PhysicalType != current.PhysicalType || previous.PhysicalName != current.PhysicalName)
                Add("physical-representation", CompatibilityClassification.PotentiallyBreaking, "Format, physical type, or physical name changed; representation-dependent consumers may be affected.");
            if (!Equal(previous.Extensions, current.Extensions))
                Add("field-extensions", CompatibilityClassification.Unknown, "Source-specific field extensions changed and need a dedicated rule.", "extensions");
            var before = Index(previous.Constraints);
            var after = Index(current.Constraints);
            Bounds("minimum", "exclusiveMinimum", true);
            Bounds("maximum", "exclusiveMaximum", false);
            foreach (var kind in before.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (kind is "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum") continue;
                before.TryGetValue(kind, out var left);
                after.TryGetValue(kind, out var right);
                if (Equal(left, right)) continue;
                var classification = Constraint(kind, left, right);
                Add("constraint:" + kind, classification, classification switch
                {
                    CompatibilityClassification.NonBreaking => "The candidate preserves or strengthens this producer guarantee.",
                    CompatibilityClassification.Breaking => "The candidate permits data outside the baseline guarantee.",
                    _ => "Constraint implication cannot be established by this policy."
                }, "constraints", kind);
            }

            void Bounds(string inclusive, string exclusive, bool lower)
            {
                if (Equal(before.GetValueOrDefault(inclusive), after.GetValueOrDefault(inclusive))
                    && Equal(before.GetValueOrDefault(exclusive), after.GetValueOrDefault(exclusive))) return;
                var classification = !Bound(before, inclusive, exclusive, lower, out var left, out var leftExclusive)
                    || !Bound(after, inclusive, exclusive, lower, out var right, out var rightExclusive)
                    ? CompatibilityClassification.Unknown
                    : left is null ? CompatibilityClassification.NonBreaking
                    : right is null ? CompatibilityClassification.Breaking
                    : (lower ? right > left : right < left) || right == left && (!leftExclusive || rightExclusive)
                        ? CompatibilityClassification.NonBreaking : CompatibilityClassification.Breaking;
                Add("constraint:" + inclusive, classification, classification switch
                {
                    CompatibilityClassification.NonBreaking => "The candidate preserves or tightens this numeric bound.",
                    CompatibilityClassification.Breaking => "The candidate relaxes this numeric bound beyond the baseline guarantee.",
                    _ => "Numeric bound implication cannot be established."
                }, "constraints", inclusive);
            }
        }
    }

    private static Dictionary<string, object?> Index(IReadOnlyList<DataConstraint> constraints)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var constraint in constraints)
            if (string.IsNullOrEmpty(constraint.Kind) || !result.TryAdd(constraint.Kind, constraint.Value))
                throw new ArgumentException("Constraint kinds must be non-empty and unique.");
        return result;
    }

    private static CompatibilityClassification Type(string? previous, string? current)
    {
        if (!KnownType(previous) || !KnownType(current)) return CompatibilityClassification.Unknown;
        if (previous == "number" && current == "integer") return CompatibilityClassification.NonBreaking;
        if (previous == "integer" && current == "number") return CompatibilityClassification.Breaking;
        if (previous is "timestamp" or "datetime" && current is "timestamp" or "datetime") return CompatibilityClassification.NonBreaking;
        return CompatibilityClassification.Breaking;
    }

    private static string TypeExplanation(string? previous, string? current) => Type(previous, current) switch
    {
        CompatibilityClassification.NonBreaking => "The candidate type is a subset of the baseline's accepted logical values.",
        CompatibilityClassification.Breaking => "The candidate type permits logical values outside the baseline type.",
        _ => "The logical type implication is unspecified or unsupported."
    };

    private static CompatibilityClassification Constraint(string kind, object? previous, object? current)
    {
        if (!KnownConstraint(kind)) return CompatibilityClassification.Unknown;
        if (kind is "pattern" or "required") return CompatibilityClassification.Unknown;
        if (kind is "unique" or "uniqueItems" or "primaryKey")
        {
            if (!Boolean(previous, out var before) || !Boolean(current, out var after)) return CompatibilityClassification.Unknown;
            return before && !after ? CompatibilityClassification.Breaking : CompatibilityClassification.NonBreaking;
        }
        if (kind == "enum")
        {
            if (current is null) return CompatibilityClassification.Breaking;
            if (current is not IEnumerable values || current is string) return CompatibilityClassification.Unknown;
            if (previous is null) return CompatibilityClassification.NonBreaking;
            if (previous is not IEnumerable baseline || previous is string) return CompatibilityClassification.Unknown;
            var allowed = baseline.Cast<object?>().ToArray();
            return values.Cast<object?>().All(value => allowed.Any(old => Equal(old, value)
                || Number(old, out var oldNumber) && Number(value, out var newNumber) && oldNumber == newNumber))
                ? CompatibilityClassification.NonBreaking : CompatibilityClassification.Breaking;
        }
        if (previous is null) return Number(current, out _) ? CompatibilityClassification.NonBreaking : CompatibilityClassification.Unknown;
        if (current is null) return Number(previous, out _) ? CompatibilityClassification.Breaking : CompatibilityClassification.Unknown;
        if (!Number(previous, out var left) || !Number(current, out var right)) return CompatibilityClassification.Unknown;
        var minimum = kind is "minimum" or "exclusiveMinimum" or "minLength" or "minItems" or "minProperties";
        return (minimum ? right >= left : right <= left) ? CompatibilityClassification.NonBreaking : CompatibilityClassification.Breaking;
    }

    private static bool Boolean(object? value, out bool result)
    {
        result = false;
        if (value is null) return true;
        if (value is bool boolean) { result = boolean; return true; }
        return bool.TryParse(value.ToString(), out result);
    }

    private static bool IsItems(ContractPath path)
    {
        var segments = new List<string>();
        var segment = new System.Text.StringBuilder();
        var escaped = false;
        foreach (var character in path.Value)
        {
            if (escaped) { segment.Append(character); escaped = false; }
            else if (character == '\\') escaped = true;
            else if (character == '.') { segments.Add(segment.ToString()); segment.Clear(); }
            else segment.Append(character);
        }
        segments.Add(segment.ToString());
        // Consume property identifiers as data, including identifiers literally named
        // "items" or "properties". Only traversal segments denote array items.
        var lastWasItems = false;
        for (var index = 2; index < segments.Count;)
        {
            if (segments[index] == "properties" && index + 1 < segments.Count)
            {
                index += 2;
                lastWasItems = false;
            }
            else if (segments[index] == "items") { index++; lastWasItems = true; }
            else return false;
        }
        return lastWasItems;
    }

    private static bool Bound(Dictionary<string, object?> constraints, string inclusive, string exclusive, bool lower,
        out decimal? bound, out bool isExclusive)
    {
        bound = null;
        isExclusive = false;
        foreach (var kind in new[] { inclusive, exclusive })
        {
            if (!constraints.TryGetValue(kind, out var value)) continue;
            if (!Number(value, out var number)) return false;
            if (bound is null || (lower ? number > bound : number < bound) || number == bound && kind == exclusive)
            {
                bound = number;
                isExclusive = kind == exclusive;
            }
        }
        return true;
    }

    private static bool Number(object? value, out decimal result)
    {
        result = 0;
        return value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            && decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static bool Equal(object? left, object? right) => ContractDiffValues.Equal(left, right);
}
