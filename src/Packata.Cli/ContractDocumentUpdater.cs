using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.Contracts;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Packata.Cli;

internal sealed record ContractDocumentUpdate(string Content, IReadOnlyList<string> Warnings);

internal static class ContractDocumentUpdater
{
    public static ContractDocumentUpdate Update(
        ContractDocument document,
        DataAsset originalAsset,
        DataEndpoint originalEndpoint,
        DataAsset inferredAsset,
        DataEndpoint inferredEndpoint)
    {
        var warnings = new List<string>();
        var content = document.Kind switch
        {
            ContractDocumentKind.DataPackageJson => UpdateDataPackageJson(
                document.OriginalText, originalAsset.Id, inferredAsset, inferredEndpoint, warnings),
            ContractDocumentKind.DataPackageYaml => UpdateDataPackageYaml(
                document.OriginalText, originalAsset.Id, inferredAsset, inferredEndpoint, warnings),
            ContractDocumentKind.OpenDataContractYaml => UpdateOdcsYaml(
                document.OriginalText, originalAsset.Id, originalEndpoint.Id, inferredAsset, inferredEndpoint, warnings),
            _ => throw new ArgumentOutOfRangeException(nameof(document))
        };
        return new(content, warnings);
    }

    private static string UpdateDataPackageJson(
        string content,
        string assetId,
        DataAsset inferredAsset,
        DataEndpoint inferredEndpoint,
        ICollection<string> warnings)
    {
        var root = JObject.Parse(content);
        var resources = root["resources"] as JArray
            ?? throw new InvalidDataException("The Data Package document has no resources array.");
        var resource = resources.OfType<JObject>().SingleOrDefault(item => (string?)item["name"] == assetId)
            ?? throw new InvalidDataException($"Resource '{assetId}' was not found in the source document.");

        var schema = resource["schema"] as JObject;
        if (schema is null)
        {
            schema = new JObject();
            resource["schema"] = schema;
        }
        schema["fields"] = MergeJsonFields(schema["fields"] as JArray, inferredAsset.Schema!.Fields, warnings);
        var dialect = resource["dialect"] as JObject;
        if (dialect is null)
        {
            dialect = new JObject();
            resource["dialect"] = dialect;
        }
        dialect["type"] = "delimited";
        ApplyJsonDialect(dialect, inferredEndpoint.Format!.Options);
        return root.ToString(Formatting.Indented) + Environment.NewLine;
    }

    private static JArray MergeJsonFields(
        JArray? existing,
        IReadOnlyList<DataField> inferred,
        ICollection<string> warnings)
    {
        var byName = (existing ?? []).OfType<JObject>()
            .Where(field => field["name"]?.Type == JTokenType.String)
            .GroupBy(field => (string)field["name"]!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var result = new JArray();
        foreach (var field in inferred)
        {
            var node = byName.TryGetValue(field.Name, out var current)
                ? (JObject)current.DeepClone()
                : new JObject { ["name"] = field.Name };
            node["type"] = ToDataPackageType(field.LogicalType, warnings);
            if (field.Format is null) node.Remove("format"); else node["format"] = field.Format;
            var constraints = node["constraints"] as JObject ?? new JObject();
            constraints["required"] = field.Required;
            node["constraints"] = constraints;
            result.Add(node);
        }
        return result;
    }

    private static void ApplyJsonDialect(JObject dialect, IReadOnlyDictionary<string, object?> options)
    {
        foreach (var name in DataPackageDialectOptions)
        {
            if (!options.TryGetValue(name, out var value)) continue;
            dialect[name] = value is null ? JValue.CreateNull() : JToken.FromObject(value);
        }
    }

    private static string UpdateDataPackageYaml(
        string content,
        string assetId,
        DataAsset inferredAsset,
        DataEndpoint inferredEndpoint,
        ICollection<string> warnings)
    {
        var yaml = LoadYaml(content);
        var root = Root(yaml);
        var resources = Sequence(root, "resources");
        var resource = FindByScalar(resources, "name", assetId)
            ?? throw new InvalidDataException($"Resource '{assetId}' was not found in the source document.");
        var schema = Mapping(resource, "schema", create: true)!;
        Set(schema, "fields", MergeDataPackageYamlFields(Sequence(schema, "fields", create: true),
            inferredAsset.Schema!.Fields, warnings));
        var dialect = Mapping(resource, "dialect", create: true)!;
        SetScalar(dialect, "type", "delimited");
        ApplyYamlDialect(dialect, inferredEndpoint.Format!.Options, DataPackageDialectOptions);
        return SaveYaml(yaml);
    }

    private static string UpdateOdcsYaml(
        string content,
        string assetId,
        string endpointId,
        DataAsset inferredAsset,
        DataEndpoint inferredEndpoint,
        ICollection<string> warnings)
    {
        var yaml = LoadYaml(content);
        var root = Root(yaml);
        var schemas = Sequence(root, "schema");
        var schema = FindByScalar(schemas, "name", assetId)
            ?? throw new InvalidDataException($"Schema asset '{assetId}' was not found in the source document.");
        Set(schema, "properties", MergeOdcsYamlFields(Sequence(schema, "properties", create: true),
            inferredAsset.Schema!.Fields, warnings));

        var servers = Sequence(root, "servers");
        var server = FindByScalar(servers, "server", endpointId)
            ?? throw new InvalidDataException($"Server '{endpointId}' was not found in the source document.");
        ApplyOdcsDialect(server, inferredEndpoint.Format!.Options, warnings);
        return SaveYaml(yaml);
    }

    private static YamlSequenceNode MergeDataPackageYamlFields(
        YamlSequenceNode existing,
        IReadOnlyList<DataField> inferred,
        ICollection<string> warnings)
    {
        var byName = ExistingYamlFieldsByName(existing);
        var result = new YamlSequenceNode();
        foreach (var field in inferred)
        {
            var node = byName.TryGetValue(field.Name, out var current) ? current : new YamlMappingNode();
            SetScalar(node, "name", field.Name);
            SetScalar(node, "type", ToDataPackageType(field.LogicalType, warnings));
            Remove(node, "logicalType");
            if (field.Format is null) Remove(node, "format"); else SetScalar(node, "format", field.Format);
            var constraints = Mapping(node, "constraints", create: true)!;
            SetScalar(constraints, "required", field.Required ? "true" : "false");
            result.Add(node);
        }
        return result;
    }

    private static YamlSequenceNode MergeOdcsYamlFields(
        YamlSequenceNode existing,
        IReadOnlyList<DataField> inferred,
        ICollection<string> warnings)
    {
        var byName = ExistingYamlFieldsByName(existing);
        var result = new YamlSequenceNode();
        foreach (var field in inferred)
        {
            var node = byName.TryGetValue(field.Name, out var current) ? current : new YamlMappingNode();
            SetScalar(node, "name", field.Name);
            SetScalar(node, "logicalType", ToOdcsType(field.LogicalType, warnings));
            SetScalar(node, "required", field.Required ? "true" : "false");
            if (field.Format is not null)
            {
                var options = Mapping(node, "logicalTypeOptions", create: true)!;
                SetScalar(options, "format", field.Format);
            }
            else if (Mapping(node, "logicalTypeOptions") is { } options)
            {
                Remove(options, "format");
                if (options.Children.Count == 0) Remove(node, "logicalTypeOptions");
            }
            result.Add(node);
        }
        return result;
    }

    private static Dictionary<string, YamlMappingNode> ExistingYamlFieldsByName(YamlSequenceNode existing) =>
        existing.Children.OfType<YamlMappingNode>()
            .Select(node => (Node: node, Name: Scalar(node, "name")))
            .Where(item => item.Name is not null)
            .GroupBy(item => item.Name!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Node, StringComparer.Ordinal);

    private static void ApplyYamlDialect(
        YamlMappingNode dialect,
        IReadOnlyDictionary<string, object?> options,
        IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            if (!options.TryGetValue(name, out var value)) continue;
            Set(dialect, name, YamlValue(value));
        }
    }

    private static void ApplyOdcsDialect(
        YamlMappingNode server,
        IReadOnlyDictionary<string, object?> options,
        ICollection<string> warnings)
    {
        var properties = Sequence(server, "customProperties", create: true);
        foreach (var (option, property) in OdcsDialectOptions)
        {
            if (!options.TryGetValue(option, out var value)) continue;
            var matches = properties.Children.OfType<YamlMappingNode>().Where(node =>
                string.Equals(Scalar(node, "vendor"), "packata", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Scalar(node, "property"), property, StringComparison.OrdinalIgnoreCase)).ToArray();
            var target = matches.FirstOrDefault() ?? new YamlMappingNode();
            if (matches.Length == 0) properties.Add(target);
            SetScalar(target, "vendor", "packata");
            SetScalar(target, "property", property);
            SetScalar(target, "value", ScalarValue(value));
        }

        var unsupported = new[] { "escapeChar", "nullSequence", "commentChar" }
            .Where(name => options.TryGetValue(name, out var value) && value is not null).ToArray();
        if (options.TryGetValue("skipInitialSpace", out var skip) && skip is true)
            unsupported = [.. unsupported, "skipInitialSpace"];
        if (unsupported.Length > 0)
            warnings.Add($"ODCS cannot represent inferred CSV options {string.Join(", ", unsupported)} directly; they were not written.");
    }

    private static string ToDataPackageType(string? logicalType, ICollection<string> warnings)
    {
        if (logicalType == "timestamp")
        {
            warnings.Add("Data Package represents the inferred timestamp field as datetime.");
            return "datetime";
        }
        return logicalType ?? "any";
    }

    private static string ToOdcsType(string? logicalType, ICollection<string> warnings)
    {
        if (logicalType == "datetime")
        {
            warnings.Add("ODCS represents the inferred datetime field as timestamp without inferred timezone semantics.");
            return "timestamp";
        }
        return logicalType ?? "string";
    }

    private static YamlStream LoadYaml(string content)
    {
        var yaml = new YamlStream();
        using var reader = new StringReader(content);
        yaml.Load(reader);
        return yaml;
    }

    private static YamlMappingNode Root(YamlStream yaml) =>
        yaml.Documents.Count == 1 && yaml.Documents[0].RootNode is YamlMappingNode root
            ? root
            : throw new InvalidDataException("The YAML contract must contain one mapping document.");

    private static string SaveYaml(YamlStream yaml)
    {
        using var writer = new StringWriter();
        yaml.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private static YamlMappingNode? Mapping(YamlMappingNode parent, string key, bool create = false)
    {
        if (parent.Children.TryGetValue(new YamlScalarNode(key), out var value))
            return value as YamlMappingNode
                ?? throw new InvalidDataException($"YAML property '{key}' must be a mapping.");
        if (!create) return null;
        var mapping = new YamlMappingNode();
        Set(parent, key, mapping);
        return mapping;
    }

    private static YamlSequenceNode Sequence(YamlMappingNode parent, string key, bool create = false)
    {
        if (parent.Children.TryGetValue(new YamlScalarNode(key), out var value))
            return value as YamlSequenceNode
                ?? throw new InvalidDataException($"YAML property '{key}' must be a sequence.");
        if (!create) throw new InvalidDataException($"YAML property '{key}' was not found.");
        var sequence = new YamlSequenceNode();
        Set(parent, key, sequence);
        return sequence;
    }

    private static YamlMappingNode? FindByScalar(YamlSequenceNode sequence, string key, string value) =>
        sequence.Children.OfType<YamlMappingNode>().SingleOrDefault(node => Scalar(node, key) == value);

    private static string? Scalar(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? (value as YamlScalarNode)?.Value : null;

    private static void SetScalar(YamlMappingNode mapping, string key, string? value) =>
        Set(mapping, key, new YamlScalarNode(value) { Style = ScalarStyle.Any });

    private static void Set(YamlMappingNode mapping, string key, YamlNode value) =>
        mapping.Children[new YamlScalarNode(key)] = value;

    private static void Remove(YamlMappingNode mapping, string key) =>
        mapping.Children.Remove(new YamlScalarNode(key));

    private static string? ScalarValue(object? value) => value switch
    {
        null => null,
        bool boolean => boolean ? "true" : "false",
        char character => character.ToString(),
        _ => value.ToString()
    };

    private static YamlNode YamlValue(object? value)
    {
        if (value is System.Collections.IEnumerable values and not string)
        {
            var sequence = new YamlSequenceNode();
            foreach (var item in values) sequence.Add(YamlValue(item));
            return sequence;
        }
        return new YamlScalarNode(ScalarValue(value)) { Style = ScalarStyle.Any };
    }

    private static readonly string[] DataPackageDialectOptions =
    [
        "header", "headerRows", "headerJoin", "headerRepeat", "commentRows", "commentChar",
        "delimiter", "lineTerminator", "quoteChar", "doubleQuote", "escapeChar", "nullSequence",
        "skipInitialSpace"
    ];

    private static readonly (string Option, string Property)[] OdcsDialectOptions =
    [
        ("delimiter", "delimiter"), ("quoteChar", "quoteChar"),
        ("lineTerminator", "lineTerminator"), ("header", "header")
    ];
}
