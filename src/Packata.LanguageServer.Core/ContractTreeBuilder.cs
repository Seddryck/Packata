using Packata.Core.Contracts;
using Packata.Documents;
using YamlDotNet.RepresentationModel;

namespace Packata.LanguageServer.Core;

/// <summary>Builds the editor-independent navigation model alongside authoritative syntax locations.</summary>
public sealed class ContractTreeBuilder(YamlMappingNode root, bool odcs, Uri uri)
{
    private const string GroupKind = "group";
    private const string QualityKind = "quality";
    private readonly SourceIndex source = new(root);
    private string UriText => uri.AbsoluteUri;
    private bool Odcs => odcs;

    public ContractTreeBuilder(ContractDocument document, Uri uri) : this(document.Root, document.Format == "odcs", uri) { }

    public ContractNode BuildNative()
    {
        var label = (ContractDocument.Child(root, "name") as YamlScalarNode)?.Value
            ?? (ContractDocument.Child(root, "id") as YamlScalarNode)?.Value ?? "Contract";
        return Node("/", label, "contract", "", NativeChildren(root, "/native", "metadata", ""),
            Odcs ? "ODCS" : "Data Package v2");
    }

    public ContractNode Build(DataContract contract)
    {
        var children = new List<ContractNode>();
        AddNative(children, "Overview", "/overview", "overview", "id", "name", "title", "version", "status", "description", "domain", "tags");
        var assets = contract.Assets.Select((asset, index) => Asset(asset, index)).ToArray();
        if (assets.Length > 0) children.Add(Node("/assets", "Assets", GroupKind, null, assets));
        AddNative(children, "Servers / endpoints", "/endpoints", "endpoint", "servers");
        AddNative(children, "Contract-level quality rules (source)", "/quality", QualityKind, QualityKind);
        AddNative(children, "SLA commitments", "/sla", "sla", "slaProperties");
        AddNative(children, "Governance", "/governance", "governance", "team", "roles", "support", "authoritativeDefinitions");
        if (!Odcs) AddNative(children, "Package metadata", "/metadata", "metadata", "contributors", "licenses", "sources", "profile", "created", "keywords");
        children.Add(Node("/source", "Source properties (including unmapped)", GroupKind, "", BuildNative().Children));
        return Node("/", contract.Metadata.Title ?? contract.Identity.Name ?? contract.Identity.Id, "contract", "",
            children, Odcs ? "ODCS" : "Data Package v2");
    }

    private ContractNode Asset(DataAsset asset, int index)
    {
        var path = Odcs ? $"/schema/{index}" : $"/resources/{index}";
        // Include an occurrence suffix only when names are duplicated, keeping normal IDs stable across reorderings.
        var siblings = source.Find(Odcs ? "/schema" : "/resources") as YamlSequenceNode;
        var duplicate = siblings?.Children.Count(node => (ContractDocument.Child(node, "name") as YamlScalarNode)?.Value == asset.Id) > 1;
        var id = "/assets/" + SourceIndex.Escape(asset.Id) + (duplicate ? $"/{index}" : "");
        var children = new List<ContractNode>();
        if (asset.Schema is { } schema)
        {
            var fieldPath = path + (Odcs ? "/properties" : "/schema/fields");
            var columns = schema.Fields.Select((field, i) => Field(field, schema.PrimaryKey, $"{id}/fields/{SourceIndex.Escape(field.Name)}/{i}", $"{fieldPath}/{i}")).ToArray();
            if (columns.Length > 0) children.Add(Node(id + "/columns", "Columns", GroupKind, fieldPath, columns));
            AddNativeAt(children, "Constraints", id + "/constraints", "constraint", path,
                Odcs ? ["relationships"] : ["schema/primaryKey", "schema/foreignKeys", "schema/uniqueKeys", "schema/fieldsMatch"]);
        }
        AddNativeAt(children, "Quality rules (source)", id + "/quality", QualityKind, path, QualityKind);
        AddNativeAt(children, "Source / endpoints", id + "/endpoints", "endpoint", path,
            Odcs ? ["physicalName", "physicalType"] : ["path", "connection", "format", "mediatype", "dialect"]);
        AddNativeAt(children, "Metadata", id + "/metadata", "metadata", path, "description", "customProperties");
        return Node(id, asset.Name, "asset", path, children, asset.Description);
    }

    private ContractNode Field(DataField field, IReadOnlyList<string> keys, string id, string path)
    {
        var children = new List<ContractNode>();
        if (field.Constraints.Count > 0)
        {
            children.Add(Node(id + "/constraints", "Constraints", GroupKind, null,
                field.Constraints.Select((constraint, index) => Node(id + $"/constraints/{index}", constraint.Kind,
                    "constraint", ConstraintPath(path, constraint.Kind), [], SafeValue(constraint.Value))).ToArray()));
        }
        AddNativeAt(children, "Quality rules (source)", id + "/quality", QualityKind, path, QualityKind);
        children.AddRange(field.Children.Select((child, index) => Field(child, [], id + $"/fields/{SourceIndex.Escape(child.Name)}/{index}", path + $"/properties/{index}")));
        if (field.Items is { } items) children.Add(Field(items, [], id + "/items", path + "/items"));
        var key = keys.Contains(field.Name) ? " [key]" : "";
        return Node(id, field.Name, "field", path, children, (field.LogicalType ?? field.PhysicalType ?? "unknown") + key);
    }

    private string? ConstraintPath(string path, string kind)
    {
        var candidates = Odcs ? new[] { path + "/" + kind, path + "/logicalTypeOptions/" + kind }
            : [path + "/constraints/" + kind];
        return candidates.FirstOrDefault(value => source.Find(value) is not null);
    }

    private void AddNative(List<ContractNode> nodes, string label, string id, string kind, params string[] names)
        => AddNativeAt(nodes, label, id, kind, "", names);

    private void AddNativeAt(List<ContractNode> nodes, string label, string id, string kind, string parent, params string[] names)
    {
        var children = names.Select(name => (name, pointer: parent + "/" + name))
            .Select(item => (item.name, item.pointer, value: source.Find(item.pointer)))
            .Where(item => item.value is not null && Meaningful(item.value))
            .Select(item => Native(item.name, id + "/" + item.name, kind, item.pointer, item.value)).ToArray();
        if (children.Length > 0) nodes.Add(Node(id, label, GroupKind, null, children,
            kind == QualityKind ? "Source declarations; execution is unavailable" : null));
    }

    private ContractNode Native(string label, string id, string kind, string sourcePointer, YamlNode? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sensitive = IsSensitive(label, value);
        var children = sensitive ? [] : NativeChildren(value, id, NativeKind(label, kind), sourcePointer);
        var description = (value as YamlScalarNode)?.Value;
        if (sensitive) description = "[redacted]";
        return Node(id, label, NativeKind(label, kind), sourcePointer, children, description) with { LogicalPath = sourcePointer };
    }

    private static string NativeKind(string label, string inherited) => label switch
    {
        "quality" => QualityKind,
        "slaProperties" => "sla",
        "constraints" or "primaryKey" or "foreignKeys" or "uniqueKeys" => "constraint",
        "servers" => "endpoint",
        _ => inherited
    };

    private static bool IsSensitive(string label, YamlNode value)
    {
        // Never project credential-bearing properties into tree descriptions.
        var namedProperty = (ContractDocument.Child(value, "property") as YamlScalarNode)?.Value
            ?? (ContractDocument.Child(value, "name") as YamlScalarNode)?.Value;
        return ContractJson.IsSensitiveProperty(label)
            || (namedProperty is not null && ContractDocument.Child(value, "value") is not null && ContractJson.IsSensitiveProperty(namedProperty));
    }

    private ContractNode[] NativeChildren(YamlNode value, string id, string kind, string sourcePointer) => value switch
    {
        YamlMappingNode map => MappingChildren(map, id, kind, sourcePointer),
        YamlSequenceNode sequence => SequenceChildren(sequence, id, kind, sourcePointer),
        _ => []
    };

    private ContractNode[] SequenceChildren(YamlSequenceNode sequence, string id, string kind, string sourcePointer)
    {
        var names = sequence.Children.Select(child => (ContractDocument.Child(child, "name") as YamlScalarNode)?.Value).ToArray();
        var counts = names.OfType<string>().CountBy(name => name).ToDictionary();
        return sequence.Children.Select((child, index) =>
        {
            var name = names[index];
            var key = name is not null && counts[name] == 1 ? "name:" + name : $"index:{index}";
            var node = Native(name ?? $"{index + 1}", SourceIndex.Append(id, key), kind,
                SourceIndex.Append(sourcePointer, $"{index}"), child);
            return node with { Kind = SequenceItemKind(sourcePointer, kind) };
        }).ToArray();
    }

    private static string SequenceItemKind(string sourcePointer, string inherited)
    {
        if (sourcePointer is "/schema" or "/resources") return "asset";
        if (sourcePointer.EndsWith("/fields", StringComparison.Ordinal)
            || sourcePointer.EndsWith("/properties", StringComparison.Ordinal)) return "field";
        return inherited;
    }

    private ContractNode[] MappingChildren(YamlMappingNode map, string id, string kind, string sourcePointer)
        => map.Children.Select(pair => (name: ((YamlScalarNode)pair.Key).Value ?? "", pair.Value))
            .Select(pair => Native(pair.name, SourceIndex.Append(id, pair.name), kind, SourceIndex.Append(sourcePointer, pair.name), pair.Value)).ToArray();

    private ContractNode Node(string id, string label, string kind, string? path, IReadOnlyList<ContractNode> children, string? description = null)
        => new(UriText + "#" + id, ContractJson.Redact(label), kind, id, ContractJson.Redact(description ?? ""),
            path is not null && source.Range(path) is { } range ? new(UriText, range) : null, children, path);

    private static bool Meaningful(YamlNode value) => value switch
    {
        YamlScalarNode scalar => !string.IsNullOrWhiteSpace(scalar.Value) && scalar.Value is not ("null" or "~"),
        YamlMappingNode map => map.Children.Count > 0,
        YamlSequenceNode sequence => sequence.Children.Count > 0,
        _ => false
    };
    private static string SafeValue(object? value) => ContractJson.RedactedJson(value ?? "").ToString(Newtonsoft.Json.Formatting.None);
}
