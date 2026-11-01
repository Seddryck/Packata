using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Packata.OpenDataContract.Serialization.Yaml;

/// <summary>Preserves scalar kinds for recognized extension values without changing other vendors' metadata.</summary>
internal static class PackataPropertyValues
{
    public static void Normalize(DataContract contract, string yaml)
    {
        if (!contract.Schema.Any(schema => Recognized(schema.CustomProperties) || schema.Properties.Any(RecognizedField))) return;
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents[0].RootNode is not YamlMappingNode root || Get(root, "schema") is not YamlSequenceNode schema) return;
        for (var index = 0; index < contract.Schema.Count; index++)
        {
            if (schema.Children[index] is YamlMappingNode node)
            {
                Properties(contract.Schema[index].CustomProperties, node);
                Fields(contract.Schema[index].Properties, node);
            }
        }
    }

    private static bool Recognized(CustomProperties properties) => properties.Any(property =>
        property.Vendor == "packata" && property.Property is "categories" or "categoryFieldMap" or "allowAdditionalProperties" or "enforcePropertyOrder");

    private static bool RecognizedField(SchemaProperty field) => Recognized(field.CustomProperties) ||
        field.Properties.Any(RecognizedField) || (field.Items is not null && RecognizedField(field.Items));

    private static void Fields(IReadOnlyList<SchemaProperty> fields, YamlMappingNode owner)
    {
        if (Get(owner, "properties") is not YamlSequenceNode sequence) return;
        for (var index = 0; index < fields.Count; index++)
            if (sequence.Children[index] is YamlMappingNode node) Field(fields[index], node);
    }

    private static void Field(SchemaProperty field, YamlMappingNode node)
    {
        Properties(field.CustomProperties, node);
        Fields(field.Properties, node);
        if (field.Items is not null && Get(node, "items") is YamlMappingNode items) Field(field.Items, items);
    }

    private static void Properties(CustomProperties properties, YamlMappingNode owner)
    {
        if (Get(owner, "customProperties") is not YamlSequenceNode sequence) return;
        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            if (property.Vendor != "packata" || property.Property is not ("categories" or "categoryFieldMap" or "allowAdditionalProperties" or "enforcePropertyOrder") ||
                sequence.Children[index] is not YamlMappingNode node || Get(node, "value") is not { } value) continue;
            using var writer = new StringWriter();
            new YamlStream(new YamlDocument(value)).Save(writer, assignAnchors: false);
            property.Value = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build()
                .Deserialize<object>(writer.ToString());
        }
    }

    private static YamlNode? Get(YamlMappingNode node, string name)
        => node.Children.TryGetValue(new YamlScalarNode(name), out var value) ? value : null;
}
