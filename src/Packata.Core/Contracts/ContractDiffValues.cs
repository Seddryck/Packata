using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Packata.Core.Contracts;

// Snapshot the canonical graph so that a caller's mutable collections cannot alter a diff.
internal static class ContractDiffValues
{
    private static IReadOnlyList<T> List<T>(IEnumerable<T> values, Func<T, T>? copy = null)
        => Array.AsReadOnly(values.Select(x => copy is null ? x : copy(x)).ToArray());

    internal static DataContract Snapshot(DataContract value) => value with
    {
        Metadata = value.Metadata with { Tags = List(value.Metadata.Tags) },
        Assets = List(value.Assets, Asset),
        Endpoints = List(value.Endpoints, Endpoint),
        Governance = value.Governance with
        {
            Ownership = value.Governance.Ownership is { } owner ? owner with { Roles = List(owner.Roles) } : null,
            ServiceLevels = List(value.Governance.ServiceLevels, sla => sla with { Value = Value(sla.Value) }),
            References = List(value.Governance.References),
            Extensions = Extensions(value.Governance.Extensions)
        },
        Extensions = Extensions(value.Extensions)
    };

    private static DataAsset Asset(DataAsset value) => value with
    {
        Schema = value.Schema is { } schema ? schema with
        {
            Fields = List(schema.Fields, Field),
            PrimaryKey = List(schema.PrimaryKey),
            Relationships = List(schema.Relationships, relationship => relationship with
            {
                Fields = List(relationship.Fields), TargetFields = List(relationship.TargetFields),
                Extensions = Extensions(relationship.Extensions)
            }),
            Extensions = Extensions(schema.Extensions)
        } : null,
        EndpointBindings = List(value.EndpointBindings),
        QualityRules = List(value.QualityRules, rule => rule.Snapshot()),
        Extensions = Extensions(value.Extensions)
    };

    private static DataField Field(DataField value) => value with
    {
        Children = List(value.Children, Field),
        Items = value.Items is { } items ? Field(items) : null,
        Constraints = List(value.Constraints, constraint => constraint with { Value = Value(constraint.Value) }),
        QualityRules = List(value.QualityRules, rule => rule.Snapshot()),
        Extensions = Extensions(value.Extensions)
    };

    private static DataEndpoint Endpoint(DataEndpoint value) => value with
    {
        Location = value.Location switch
        {
            PathLocation path => path with { Paths = List(path.Paths) },
            InlineLocation inline => inline with { Value = Value(inline.Value) },
            ConnectionLocation connection => connection,
            _ => throw new NotSupportedException($"Unsupported location type '{value.Location.GetType()}'.")
        },
        Format = value.Format is { } format ? format with { Options = Dictionary(format.Options) } : null,
        Extensions = Extensions(value.Extensions)
    };

    private static ExtensionMetadata Extensions(ExtensionMetadata value)
        => new(new ReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>(
            value.Namespaces.ToDictionary(key => key, key => Dictionary(value[key]), StringComparer.Ordinal)));

    private static IReadOnlyDictionary<string, object?> Dictionary(IEnumerable<KeyValuePair<string, object?>> values)
        => new ReadOnlyDictionary<string, object?>(values.ToDictionary(x => x.Key, x => Value(x.Value), StringComparer.Ordinal));

    private static object? Value(object? value) => value switch
    {
        null => null,
        string or bool or char or byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal or DateTime or DateTimeOffset or DateOnly or TimeOnly
            or TimeSpan or Guid or Uri or Enum => value,
        JsonElement element => ValueFromJson(element),
        IReadOnlyDictionary<string, object?> dictionary => Dictionary(dictionary),
        IDictionary dictionary => Dictionary(dictionary.Keys.Cast<object>().Select(key =>
            new KeyValuePair<string, object?>(key as string
                ?? throw new NotSupportedException("Contract diff dictionary keys must be strings."), dictionary[key]))),
        IEnumerable sequence => Array.AsReadOnly(sequence.Cast<object?>().Select(Value).ToArray()),
        _ => throw new NotSupportedException($"Cannot snapshot mutable or unsupported contract value '{value.GetType()}'.")
    };

    private static object? ValueFromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? (object)integer
            : element.TryGetDecimal(out var number) ? number : element.GetDouble(),
        JsonValueKind.Array => Array.AsReadOnly(element.EnumerateArray().Select(ValueFromJson).ToArray()),
        JsonValueKind.Object => Dictionary(element.EnumerateObject().Select(property =>
            new KeyValuePair<string, object?>(property.Name, ValueFromJson(property.Value)))),
        _ => throw new NotSupportedException()
    };

    internal static bool Equal(object? previous, object? current)
    {
        if (ReferenceEquals(previous, current)) return true;
        if (previous is null || current is null) return false;
        if (previous is ExtensionMetadata leftExtensions && current is ExtensionMetadata rightExtensions)
            return Equal(leftExtensions.Namespaces.Order(StringComparer.Ordinal).ToArray(), rightExtensions.Namespaces.Order(StringComparer.Ordinal).ToArray())
                && leftExtensions.Namespaces.All(key => Equal(leftExtensions[key], rightExtensions[key]));
        if (previous is IReadOnlyDictionary<string, object?> leftDictionary && current is IReadOnlyDictionary<string, object?> rightDictionary)
            return leftDictionary.Count == rightDictionary.Count && leftDictionary.All(pair =>
                rightDictionary.TryGetValue(pair.Key, out var value) && Equal(pair.Value, value));
        if (previous is IEnumerable left && current is IEnumerable right && previous is not string && current is not string)
        {
            var leftValues = left.Cast<object?>().ToArray();
            var rightValues = right.Cast<object?>().ToArray();
            return leftValues.Length == rightValues.Length && leftValues.Zip(rightValues).All(pair => Equal(pair.First, pair.Second));
        }
        if (previous.GetType() != current.GetType()) return false;
        // Canonical records contain collections whose record equality is reference-based.
        if (previous is not Enum && previous.GetType().Namespace == typeof(DataContract).Namespace)
            return previous.GetType().GetProperties().All(property => Equal(property.GetValue(previous), property.GetValue(current)));
        return previous.Equals(current);
    }
}
