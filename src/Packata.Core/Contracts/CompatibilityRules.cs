using System.Collections;
using System.Globalization;

namespace Packata.Core.Contracts;

internal static class CompatibilityRules
{
    // Shared domain containment primitives: accepted baseline versus offered candidate.
    internal static CompatibilityClassification CompareTypes(string? accepted, string? offered)
        => KnownType(accepted) && accepted == offered ? CompatibilityClassification.NonBreaking
            : ChangeClassifier.Type(accepted, offered);

    internal static CompatibilityClassification CompareConstraint(string kind, object? accepted, object? offered)
        => ChangeClassifier.Constraint(kind, accepted, offered);

    internal static CompatibilityClassification CompareCapacity(int? accepted, int? offered)
        => accepted is null || offered is null ? CompatibilityClassification.Unknown
            : offered <= accepted ? CompatibilityClassification.NonBreaking : CompatibilityClassification.Breaking;

    internal static CompatibilityClassification CompareBounds(IReadOnlyList<DataConstraint> accepted,
        IReadOnlyList<DataConstraint> offered, bool lower)
        => ChangeClassifier.ClassifyBounds(accepted.ToDictionary(x => x.Kind, x => x.Value, StringComparer.Ordinal),
            offered.ToDictionary(x => x.Kind, x => x.Value, StringComparer.Ordinal),
            lower ? Minimum : Maximum, lower ? ExclusiveMinimum : ExclusiveMaximum, lower);
    private const string Minimum = "minimum";
    private const string Maximum = "maximum";
    private const string ExclusiveMinimum = "exclusiveMinimum";
    private const string ExclusiveMaximum = "exclusiveMaximum";
    private const string Required = "required";

    internal static bool KnownType(string? type) => type is "string" or "integer" or "number" or "boolean"
        or "date" or "time" or "timestamp" or "datetime" or "object" or "array";
    internal static bool KnownConstraint(string kind) => kind is Minimum or Maximum or ExclusiveMinimum
        or ExclusiveMaximum or "minLength" or "maxLength" or "minItems" or "maxItems" or "minProperties"
        or "maxProperties" or "unique" or "uniqueItems" or "primaryKey" or "enum" or "pattern" or Required;

    internal static bool SupportedConstraintValue(DataConstraint constraint) => constraint.Kind switch
    {
        "unique" or "uniqueItems" or "primaryKey" => constraint.Value is bool,
        "enum" or Required => constraint.Value is IEnumerable and not string,
        "pattern" => constraint.Value is string,
        "minLength" or "maxLength" or "minItems" or "maxItems" or "minProperties" or "maxProperties"
            => Number(constraint.Value, out var count) && count >= 0 && count == decimal.Truncate(count),
        _ => KnownConstraint(constraint.Kind) && Number(constraint.Value, out _)
    };

    internal static IEnumerable<CompatibilityFinding> Classify(IContractChange change)
        => new ChangeClassifier(change).Analyze();

    private sealed class ChangeClassifier(IContractChange change)
    {
        private readonly List<CompatibilityFinding> findings = [];

        internal IEnumerable<CompatibilityFinding> Analyze()
        {
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
                    Dataset(dataset);
                    break;
                case ContractMetadataChanged metadata:
                    Metadata(metadata);
                    break;
                case IServerChange:
                    Add("server-access", CompatibilityClassification.PotentiallyBreaking, "Server access or representation changed; operational impact depends on the consumer."); break;
                default:
                    Add("unsupported-change", CompatibilityClassification.Unknown, "Relationship, quality, SLA, or unrecognized change semantics have no supported rule."); break;
            }
            if (findings.Count == 0)
                Add("descriptive-metadata", CompatibilityClassification.NonBreaking, "Only descriptive metadata changed; producer structural guarantees are unchanged.");
            return findings;
        }

        private void Dataset(DatasetModified dataset)
        {
            DatasetMatching(dataset.Previous.Schema, dataset.Current.Schema);
            DatasetAccess(dataset.Previous, dataset.Current);
            DatasetSemantics(dataset.Previous, dataset.Current);
        }

        private void DatasetMatching(DataSchema? previous, DataSchema? current)
        {
            if (previous?.PropertyMatching != current?.PropertyMatching)
                Add("property-matching", CompatibilityClassification.Unknown, "Native field matching mode changed; safe containment requires set/order policy.");
            if (previous?.PropertyOrder != current?.PropertyOrder)
                Add("property-order", CompatibilityClassification.Unknown, "Property ordering changed; containment requires an order policy.");
            if (SchemaPropertyComparison.ClosedSetChanged(previous, current))
                Add("property-set", CompatibilityClassification.Unknown, "Closed property-set declarations changed; the open-record assumption cannot prove compatibility.");
            if (SchemaPropertyComparison.OrderChanged(previous, current))
                Add("property-sequence", CompatibilityClassification.Unknown, "Declared property sequence changed; order containment is unresolved.");
        }

        private void DatasetAccess(DataAsset previous, DataAsset current)
        {
            if (previous.Name != current.Name || previous.PhysicalName != current.PhysicalName
                || previous.Kind != current.Kind || !Equal(previous.EndpointBindings, current.EndpointBindings))
                Add("dataset-access", CompatibilityClassification.PotentiallyBreaking, "Dataset naming, representation, or endpoint bindings changed; consumer access assumptions may be affected.");
        }

        private void DatasetSemantics(DataAsset previous, DataAsset current)
        {
            if (!Equal(previous.Schema?.PrimaryKey, current.Schema?.PrimaryKey)
                || !Equal(previous.Schema?.Extensions, current.Schema?.Extensions)
                || !Equal(previous.Extensions, current.Extensions)
                || (previous.Schema is null) != (current.Schema is null))
                Add("dataset-semantics", CompatibilityClassification.Unknown, "Schema keys, schema availability, or extension semantics require additional policy rules.");
        }

        private void Metadata(ContractMetadataChanged metadata)
        {
            if (metadata.Path.Value == "metadata") return;
            if (metadata.Previous is not ContractIdentity previous || metadata.Current is not ContractIdentity current)
            {
                Add("contract-metadata", CompatibilityClassification.Unknown, "Governance or extension metadata may carry semantics this policy cannot classify.");
                return;
            }
            if (previous.Id != current.Id || previous.Name != current.Name)
                Add("contract-identity", CompatibilityClassification.PotentiallyBreaking, "Contract identity changed and may affect consumer discovery.");
        }

        private void Add(string rule, CompatibilityClassification classification, string explanation, string? facet = null, string? key = null)
        {
            var path = facet is null ? change.Path : change.Path.Append(facet);
            findings.Add(new(key is null ? path : path.Append(key), classification, rule, explanation, change));
        }

        private void Field(DataField previous, DataField current)
        {
            if (previous.Required != current.Required)
                Add("requiredness", current.Required ? CompatibilityClassification.NonBreaking : CompatibilityClassification.Breaking,
                    current.Required ? "The candidate guarantees field presence." : "The candidate no longer guarantees field presence.", Required);
            if (previous.Nullable != current.Nullable)
                Add("nullability", current.Nullable ? CompatibilityClassification.Breaking : CompatibilityClassification.NonBreaking,
                    current.Nullable ? "Candidate data may contain nulls that baseline consumers did not accept." : "The candidate strengthens the non-null guarantee.", "nullable");
            if (previous.Format != current.Format || previous.PhysicalType != current.PhysicalType || previous.PhysicalName != current.PhysicalName)
                Add("physical-representation", CompatibilityClassification.PotentiallyBreaking, "Format, physical type, or physical name changed; representation-dependent consumers may be affected.");
            if (!Equal(previous.Extensions, current.Extensions))
                Add("field-extensions", CompatibilityClassification.Unknown, "Source-specific field extensions changed and need a dedicated rule.", "extensions");
            var before = Index(previous.Constraints);
            var after = Index(current.Constraints);
            Bounds(before, after, Minimum, ExclusiveMinimum, true);
            Bounds(before, after, Maximum, ExclusiveMaximum, false);
            Constraints(before, after);
        }

        private void Constraints(Dictionary<string, object?> before, Dictionary<string, object?> after)
        {
            var kinds = before.Keys.Union(after.Keys, StringComparer.Ordinal)
                .Where(kind => kind is not (Minimum or Maximum or ExclusiveMinimum or ExclusiveMaximum))
                .Order(StringComparer.Ordinal);
            foreach (var kind in kinds)
            {
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
        }

        private void Bounds(Dictionary<string, object?> before, Dictionary<string, object?> after,
            string inclusive, string exclusive, bool lower)
        {
            if (Equal(before.GetValueOrDefault(inclusive), after.GetValueOrDefault(inclusive))
                && Equal(before.GetValueOrDefault(exclusive), after.GetValueOrDefault(exclusive))) return;
            var classification = ClassifyBounds(before, after, inclusive, exclusive, lower);
            Add("constraint:" + inclusive, classification, classification switch
            {
                CompatibilityClassification.NonBreaking => "The candidate preserves or tightens this numeric bound.",
                CompatibilityClassification.Breaking => "The candidate relaxes this numeric bound beyond the baseline guarantee.",
                _ => "Numeric bound implication cannot be established."
            }, "constraints", inclusive);
        }
        internal static CompatibilityClassification ClassifyBounds(Dictionary<string, object?> before, Dictionary<string, object?> after,
            string inclusive, string exclusive, bool lower)
        {
            if (!Bound(before, inclusive, exclusive, lower, out var left, out var leftExclusive)
                || !Bound(after, inclusive, exclusive, lower, out var right, out var rightExclusive))
                return CompatibilityClassification.Unknown;
            if (left is null) return CompatibilityClassification.NonBreaking;
            if (right is null) return CompatibilityClassification.Breaking;
            if (left == right)
                return !leftExclusive || rightExclusive ? CompatibilityClassification.NonBreaking : CompatibilityClassification.Breaking;
            var tighter = lower ? right > left : right < left;
            return tighter ? CompatibilityClassification.NonBreaking : CompatibilityClassification.Breaking;
        }

        private static Dictionary<string, object?> Index(IReadOnlyList<DataConstraint> constraints)
            => constraints.ToDictionary(constraint => NonEmptyKind(constraint.Kind), constraint => constraint.Value, StringComparer.Ordinal);

        private static string NonEmptyKind(string kind)
        {
            ArgumentException.ThrowIfNullOrEmpty(kind);
            return kind;
        }

        internal static CompatibilityClassification Type(string? previous, string? current)
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

        internal static CompatibilityClassification Constraint(string kind, object? previous, object? current)
        {
            if (!KnownConstraint(kind)) return CompatibilityClassification.Unknown;
            if (kind is "pattern" or Required) return CompatibilityClassification.Unknown;
            if (kind is "unique" or "uniqueItems" or "primaryKey")
                return BooleanConstraint(previous, current);
            if (kind == "enum")
                return EnumConstraint(previous, current);
            return NumericConstraint(kind, previous, current);
        }

        private static CompatibilityClassification BooleanConstraint(object? previous, object? current)
        {
            if (!Boolean(previous, out var before) || !Boolean(current, out var after)) return CompatibilityClassification.Unknown;
            return before && !after ? CompatibilityClassification.Breaking : CompatibilityClassification.NonBreaking;
        }

        private static CompatibilityClassification EnumConstraint(object? previous, object? current)
        {
            if (current is null) return CompatibilityClassification.Breaking;
            if (current is not IEnumerable values || current is string) return CompatibilityClassification.Unknown;
            if (previous is null) return CompatibilityClassification.NonBreaking;
            if (previous is not IEnumerable baseline || previous is string) return CompatibilityClassification.Unknown;
            var allowed = baseline.Cast<object?>().ToArray();
            return values.Cast<object?>().All(value => allowed.Any(old => EnumValueEqual(old, value)))
                ? CompatibilityClassification.NonBreaking : CompatibilityClassification.Breaking;
        }

        private static bool EnumValueEqual(object? previous, object? current) => Equal(previous, current)
            || Number(previous, out var left) && Number(current, out var right) && left == right;

        private static CompatibilityClassification NumericConstraint(string kind, object? previous, object? current)
        {
            if (previous is null) return Number(current, out _) ? CompatibilityClassification.NonBreaking : CompatibilityClassification.Unknown;
            if (current is null) return Number(previous, out _) ? CompatibilityClassification.Breaking : CompatibilityClassification.Unknown;
            if (!Number(previous, out var left) || !Number(current, out var right)) return CompatibilityClassification.Unknown;
            var minimum = kind is Minimum or ExclusiveMinimum or "minLength" or "minItems" or "minProperties";
            var tighter = minimum ? right >= left : right <= left;
            return tighter ? CompatibilityClassification.NonBreaking : CompatibilityClassification.Breaking;
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
            var segments = PathSegments(path);
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

        private static List<string> PathSegments(ContractPath path)
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
            return segments;
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
                if (bound is null || IsTighterBound(number, bound.Value, lower) || number == bound && kind == exclusive)
                {
                    bound = number;
                    isExclusive = kind == exclusive;
                }
            }
            return true;
        }

        private static bool IsTighterBound(decimal candidate, decimal baseline, bool lower)
            => lower ? candidate > baseline : candidate < baseline;

        private static bool Equal(object? left, object? right) => ContractDiffValues.Equal(left, right);
    }

    private static bool Number(object? value, out decimal result)
    {
        result = 0;
        return value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            && decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

}
