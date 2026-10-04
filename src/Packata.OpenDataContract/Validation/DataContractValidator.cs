using System.Text.RegularExpressions;
using System.Globalization;
using Packata.OpenDataContract.Types;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Packata.OpenDataContract.Validation;

internal static partial class DataContractValidator
{
    public static void Validate(DataContract contract)
    {
        foreach (var schema in contract.Schema)
        {
            ValidateSynonyms(schema.Synonyms, $"schema/{schema.Name}/synonyms");
            ValidateRelationships(schema.Relationships, $"schema/{schema.Name}/relationships");
            foreach (var property in schema.Properties)
                ValidateProperty(property, $"schema/{schema.Name}/properties/{property.Name}");
        }
    }

    public static void ValidateYamlStructure(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            return;

        var synonymOwners = new HashSet<YamlMappingNode>(ReferenceEqualityComparer.Instance);
        if (TryGet(root, "schema") is YamlSequenceNode schema)
        {
            foreach (var item in schema.Children.OfType<YamlMappingNode>())
            {
                synonymOwners.Add(item);
                MarkPropertyMappings(item, synonymOwners);
            }
        }

        var serverOwners = new HashSet<YamlMappingNode>(ReferenceEqualityComparer.Instance);
        if (TryGet(root, "servers") is YamlSequenceNode servers)
        {
            foreach (var item in servers.Children.OfType<YamlMappingNode>())
                serverOwners.Add(item);
        }

        ValidatePlacement(root, "$", synonymOwners, serverOwners);
    }

    private static void ValidateProperty(SchemaProperty property, string path)
    {
        property.SemanticType ??= "column";
        if (property.SemanticType is not ("column" or "measure" or "dimension"))
            throw new YamlException($"Property '{path}' has invalid semanticType '{property.SemanticType}'; expected column, measure, or dimension.");

        ValidateSynonyms(property.Synonyms, $"{path}/synonyms");
        ValidateRelationships(property.Relationships, $"{path}/relationships");
        ValidateEnumeration(property, path);

        if (property.LogicalType is VectorLogicalType vector && vector.Dimensions is not > 0)
            throw new YamlException($"Property '{path}' uses logicalType vector and requires a positive integer logicalTypeOptions.dimensions.");

        foreach (var nested in property.Properties)
            ValidateProperty(nested, $"{path}/properties/{nested.Name}");
        if (property.Items is not null)
            ValidateProperty(property.Items, $"{path}/items/{property.Items.Name}");
        if (property.Map is not null)
        {
            ValidateProperty(property.Map.Key, $"{path}/map/key/{property.Map.Key.Name}");
            ValidateProperty(property.Map.Value, $"{path}/map/value/{property.Map.Value.Name}");
        }
    }

    private static void ValidateEnumeration(SchemaProperty property, string path)
    {
        if (property.Enum is null)
            return;
        if (property.Enum.Count == 0)
            throw new YamlException($"Property '{path}' has an enum that must contain at least one value.");

        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in property.Enum)
        {
            if (item.Value is null)
                throw new YamlException($"Property '{path}' has an enum entry without the required value.");
            if (!IsScalar(item.Value))
                throw new YamlException($"Property '{path}' has a non-scalar enum value.");
            if (!IsCompatible(item.Value, property.LogicalType))
                throw new YamlException($"Property '{path}' has enum value '{item.Value}' incompatible with its logicalType.");

            var canonical = $"{item.Value.GetType().FullName}:{item.Value}";
            if (!values.Add(canonical))
                throw new YamlException($"Property '{path}' has duplicate enum value '{item.Value}'.");
        }
    }

    private static bool IsScalar(object value)
        => value is string or char or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or DateTime or DateTimeOffset or TimeSpan or Guid;

    private static bool IsCompatible(object value, ILogicalType? logicalType)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return logicalType switch
        {
            StringLogicalType => value is string,
            IntegerLogicalType => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            NumberLogicalType => decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out _),
            BooleanLogicalType => bool.TryParse(text, out _),
            DateLogicalType => DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            TimestampLogicalType => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            TimeLogicalType => TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            MapLogicalType or VectorLogicalType or ArrayLogicalType => false,
            _ => true
        };
    }

    private static void ValidateSynonyms(IEnumerable<Synonym> synonyms, string path)
    {
        foreach (var synonym in synonyms)
        {
            if (string.IsNullOrWhiteSpace(synonym.SynonymValue))
                throw new YamlException($"Synonym at '{path}' requires a non-empty synonym value.");
        }
    }

    private static void MarkPropertyMappings(YamlMappingNode owner, HashSet<YamlMappingNode> allowed)
    {
        if (TryGet(owner, "properties") is not YamlSequenceNode properties)
            return;

        foreach (var property in properties.Children.OfType<YamlMappingNode>())
        {
            allowed.Add(property);
            MarkPropertyMappings(property, allowed);
            if (TryGet(property, "items") is YamlMappingNode items)
            {
                allowed.Add(items);
                MarkPropertyMappings(items, allowed);
            }
            if (TryGet(property, "map") is YamlMappingNode map)
            {
                foreach (var name in new[] { "key", "value" })
                {
                    if (TryGet(map, name) is not YamlMappingNode member)
                        continue;
                    allowed.Add(member);
                    MarkPropertyMappings(member, allowed);
                }
            }
        }
    }

    private static void ValidatePlacement(
        YamlNode node,
        string path,
        HashSet<YamlMappingNode> synonymOwners,
        HashSet<YamlMappingNode> serverOwners)
    {
        if (node is YamlSequenceNode sequence)
        {
            for (var index = 0; index < sequence.Children.Count; index++)
                ValidatePlacement(sequence.Children[index], $"{path}[{index}]", synonymOwners, serverOwners);
            return;
        }
        if (node is not YamlMappingNode mapping)
            return;

        if (TryGet(mapping, "synonyms") is not null && !synonymOwners.Contains(mapping))
            throw new YamlException($"The synonyms field at '{path}' is only permitted on schema objects and properties.");

        if (TryGet(mapping, "encoding") is not null)
        {
            if (!serverOwners.Contains(mapping))
                throw new YamlException($"The encoding field at '{path}' is only permitted on supported server definitions.");
            var type = (TryGet(mapping, "type") as YamlScalarNode)?.Value;
            if (type is not ("azure" or "glue" or "custom" or "kafka" or "kinesis" or "local" or "s3" or "sftp"))
                throw new YamlException($"Server at '{path}' has type '{type}' which does not permit encoding.");
        }

        foreach (var pair in mapping.Children)
        {
            var name = (pair.Key as YamlScalarNode)?.Value ?? "?";
            ValidatePlacement(pair.Value, $"{path}/{name}", synonymOwners, serverOwners);
        }
    }

    private static YamlNode? TryGet(YamlMappingNode mapping, string key)
        => mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static void ValidateRelationships(IEnumerable<Relationship> relationships, string path)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relationship in relationships)
        {
            if (relationship.Id is null)
                continue;

            if (InvalidStableIdCharacter().IsMatch(relationship.Id))
                throw new YamlException($"Relationship id '{relationship.Id}' at {path} contains a prohibited character.");

            if (!identifiers.Add(relationship.Id))
                throw new YamlException($"Relationship id '{relationship.Id}' is duplicated at {path}.");
        }
    }

    [GeneratedRegex(@"[.#/\\@!%&^\s]")]
    private static partial Regex InvalidStableIdCharacter();
}
