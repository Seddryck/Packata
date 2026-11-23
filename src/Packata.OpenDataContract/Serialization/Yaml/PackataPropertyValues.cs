using Packata.OpenDataContract.Mapping;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Packata.OpenDataContract.Serialization.Yaml;

/// <summary>Retains YAML scalar kinds for extension mapping while preserving the native metadata representation.</summary>
internal static class PackataPropertyValues
{
    public static void Normalize(DataContract contract, YamlMappingNode? root)
    {
        if (root is null || Get(root, "schema") is not YamlSequenceNode schema) return;
        for (var index = 0; index < contract.Schema.Count; index++)
        {
            if (schema.Children[index] is YamlMappingNode node)
            {
                Properties(contract.Schema[index].CustomProperties, node, normalizeNative: true);
                Fields(contract.Schema[index].Properties, node, normalizeNative: true);
            }
        }
    }

    private static void Fields(IReadOnlyList<SchemaProperty> fields, YamlMappingNode owner, bool normalizeNative)
    {
        if (Get(owner, "properties") is not YamlSequenceNode sequence) return;
        for (var index = 0; index < fields.Count; index++)
            if (sequence.Children[index] is YamlMappingNode node) Field(fields[index], node, normalizeNative);
    }

    private static void Field(SchemaProperty field, YamlMappingNode node, bool normalizeNative)
    {
        PreserveSpatialOptions(field, node);
        Properties(field.CustomProperties, node, normalizeNative);
        Fields(field.Properties, node, normalizeNative);
        if (field.Items is not null && Get(node, "items") is YamlMappingNode items) Field(field.Items, items, normalizeNative);
        if (field.Map is { } map && Get(node, "map") is YamlMappingNode mapNode) MapFields(map, mapNode);
    }

    private static void PreserveSpatialOptions(SchemaProperty field, YamlMappingNode node)
    {
        if (field.LogicalType is Types.UnknownLogicalType { Type: "geometry" or "geography" }
            && Get(node, "logicalTypeOptions") is YamlMappingNode options
            && Read(options) is System.Collections.IDictionary values)
        {
            field.PreserveSpatialOptions(values.Keys.Cast<object>().ToDictionary(
                key => (string)key, key => values[key]!));
        }
    }

    private static void MapFields(MapDefinition map, YamlMappingNode node)
    {
        if (Get(node, "key") is YamlMappingNode key) Field(map.Key, key, normalizeNative: false);
        if (Get(node, "value") is YamlMappingNode value) Field(map.Value, value, normalizeNative: false);
    }

    private static void Properties(CustomProperties properties, YamlMappingNode owner, bool normalizeNative)
    {
        if (Get(owner, "customProperties") is not YamlSequenceNode sequence) return;
        for (var index = 0; index < properties.Count; index++)
        {
            if (sequence.Children[index] is not YamlMappingNode node || Get(node, "value") is not { } value) continue;
            var property = properties[index];
            if (normalizeNative && (OdcsBindings.Default.HasTypedNativeValue(property) || IndexDeclarationMapper.IsDeclaration(property)))
                property.Value = Read(value);
            else property.PreserveMappingValue(() => Read(value));
        }
    }

    private static object? Read(YamlNode value)
    {
        using var writer = new StringWriter();
        new YamlStream(new YamlDocument(value)).Save(writer, assignAnchors: false);
        return new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build()
            .Deserialize<object>(writer.ToString());
    }

    private static YamlNode? Get(YamlMappingNode node, string name)
        => node.Children.TryGetValue(new YamlScalarNode(name), out var value) ? value : null;
}
