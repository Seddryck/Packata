namespace Packata.Core.Contracts;

/// <summary>Compares canonical contracts without applying a compatibility policy.</summary>
public static class ContractDiffer
{
    public static ContractDiff Diff(this DataContract previous, DataContract current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        Validate(previous);
        Validate(current);
        previous = ContractDiffValues.Snapshot(previous);
        current = ContractDiffValues.Snapshot(current);
        var changes = new List<IContractChange>();
        Metadata("identity", previous.Identity, current.Identity);
        Metadata("metadata", previous.Metadata, current.Metadata);
        Metadata("extensions", previous.Extensions, current.Extensions);
        Metadata("governance", previous.Governance with { ServiceLevels = [] }, current.Governance with { ServiceLevels = [] });

        Compare(previous.Endpoints, current.Endpoints, x => x.Id, ContractPath.Root("servers"),
            (path, value) => changes.Add(new ServerAdded(path, value)),
            (path, value) => changes.Add(new ServerRemoved(path, value)),
            (path, before, after) => Modified(before, after, () => changes.Add(new ServerModified(path, before, after))));
        Compare(previous.Governance.ServiceLevels, current.Governance.ServiceLevels, x => x.Name, ContractPath.Root("sla"),
            (path, value) => changes.Add(new SlaAdded(path, value)),
            (path, value) => changes.Add(new SlaRemoved(path, value)),
            (path, before, after) => Modified(before, after, () => changes.Add(new SlaModified(path, before, after))));
        Compare(previous.Assets, current.Assets, x => x.Id, ContractPath.Root("schema"),
            (path, value) => changes.Add(new DatasetAdded(path, value)),
            (path, value) => changes.Add(new DatasetRemoved(path, value)), Dataset);
        return new ContractDiff(changes);

        void Metadata(string path, object? before, object? after)
            => Modified(before, after, () => changes.Add(new ContractMetadataChanged(ContractPath.Root(path), before, after)));

        void Dataset(ContractPath path, DataAsset before, DataAsset after)
        {
            var ordered = before.Schema?.PropertyOrder == PropertyOrder.Declared || after.Schema?.PropertyOrder == PropertyOrder.Declared;
            if (ordered && !(before.Schema?.Fields.Select(x => x.Name) ?? []).SequenceEqual(after.Schema?.Fields.Select(x => x.Name) ?? [], StringComparer.Ordinal)
                || !ContractDiffValues.Equal(WithoutChildren(before), WithoutChildren(after)))
                changes.Add(new DatasetModified(path, before, after));
            Fields(path.Append("properties"), before.Id, before.Schema?.Fields ?? [], after.Schema?.Fields ?? []);
            Compare(before.Schema?.Relationships ?? [], after.Schema?.Relationships ?? [], RelationshipId, path.Append("relationships"),
                (p, value) => changes.Add(new RelationshipAdded(p, value)),
                (p, value) => changes.Add(new RelationshipRemoved(p, value)),
                (p, oldValue, newValue) => Modified(oldValue, newValue, () => changes.Add(new RelationshipModified(p, oldValue, newValue))));
            Rules(path, before.QualityRules, after.QualityRules);
        }

        void Fields(ContractPath path, string datasetId, IReadOnlyList<DataField> before, IReadOnlyList<DataField> after)
            => Compare(before, after, x => x.Name, path,
                (p, value) => changes.Add(new PropertyAdded(p, datasetId, value.Name, value)),
                (p, value) => changes.Add(new PropertyRemoved(p, datasetId, value.Name, value)),
                (p, oldValue, newValue) => Field(p, datasetId, oldValue, newValue));

        void Field(ContractPath path, string datasetId, DataField before, DataField after)
        {
            if (before.LogicalType != after.LogicalType)
                changes.Add(new PropertyTypeChanged(path.Append("type"), datasetId, before.Name, before.LogicalType, after.LogicalType));
            Modified(WithoutChildren(before), WithoutChildren(after), () => changes.Add(new PropertyModified(path, datasetId, before.Name, before, after)));
            Fields(path.Append("properties"), datasetId, before.Children, after.Children);
            var itemsPath = path.Append("items");
            if (before.Items is null && after.Items is { } added)
                changes.Add(new PropertyAdded(itemsPath, datasetId, added.Name, added));
            else if (before.Items is { } removed && after.Items is null)
                changes.Add(new PropertyRemoved(itemsPath, datasetId, removed.Name, removed));
            else if (before.Items is { } oldItems && after.Items is { } newItems)
                Field(itemsPath, datasetId, oldItems, newItems);
            Rules(path, before.QualityRules, after.QualityRules);
        }

        void Rules(ContractPath path, IReadOnlyList<DataQualityRule> before, IReadOnlyList<DataQualityRule> after)
            => Compare(before, after, RuleId, path.Append("quality"),
                (p, value) => changes.Add(new QualityRuleAdded(p, value)),
                (p, value) => changes.Add(new QualityRuleRemoved(p, value)),
                (p, oldValue, newValue) => Modified(oldValue, newValue, () => changes.Add(new QualityRuleModified(p, oldValue, newValue))));
    }

    private static DataAsset WithoutChildren(DataAsset value) => value with
    {
        Schema = value.Schema is { } schema ? schema with { Fields = [], Relationships = [] } : null,
        QualityRules = []
    };

    private static DataField WithoutChildren(DataField value)
        => value with { LogicalType = null, Children = [], Items = null, QualityRules = [] };

    // Unnamed relationships and quality rules have no canonical ID. Use their semantic
    // identity, never their collection index. A changed identity is a removal plus addition.
    private static string RelationshipId(DataRelationship value)
        => value.Name is { Length: > 0 } name ? "name:" + name
            : "definition:" + Key([value.Kind, value.TargetAsset, Key(value.Fields), Key(value.TargetFields)]);
    private static string RuleId(DataQualityRule value) => value.SourceId ?? value.SourcePath ?? Key([value.Kind, value.Expression]);
    private static string Key(IEnumerable<string?> parts)
        => string.Concat(parts.Select(part => part is null ? "-1:" : $"{part.Length}:{part}"));

    private static void Modified(object? previous, object? current, Action report)
    {
        if (!ContractDiffValues.Equal(previous, current)) report();
    }

    private static void Validate(DataContract contract)
    {
        Identifiers(contract.Assets.Select(x => x.Id), ContractPath.Root("schema"));
        Identifiers(contract.Endpoints.Select(x => x.Id), ContractPath.Root("servers"));
        Identifiers(contract.Governance.ServiceLevels.Select(x => x.Name), ContractPath.Root("sla"));
        foreach (var asset in contract.Assets)
        {
            var path = ContractPath.Root("schema").Append(asset.Id);
            Identifiers(asset.QualityRules.Select(RuleId), path.Append("quality"));
            if (asset.Schema is not { } schema) continue;
            Identifiers(schema.Relationships.Select(RelationshipId), path.Append("relationships"));
            Fields(schema.Fields, path.Append("properties"));
        }

        static void Fields(IReadOnlyList<DataField> fields, ContractPath path)
        {
            Identifiers(fields.Select(x => x.Name), path);
            foreach (var field in fields) Field(field, path.Append(field.Name));
        }

        static void Field(DataField field, ContractPath path)
        {
            Identifiers(field.QualityRules.Select(RuleId), path.Append("quality"));
            Fields(field.Children, path.Append("properties"));
            if (field.Items is { } items) Field(items, path.Append("items"));
        }

        static void Identifiers(IEnumerable<string> identifiers, ContractPath path)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var identifier in identifiers)
                if (string.IsNullOrEmpty(identifier) || !seen.Add(identifier))
                    throw new ArgumentException($"Missing or duplicate semantic identifier at '{path}': '{identifier}'.");
        }
    }

    private static void Compare<T>(IReadOnlyList<T> previous, IReadOnlyList<T> current, Func<T, string> id,
        ContractPath path, Action<ContractPath, T> added, Action<ContractPath, T> removed, Action<ContractPath, T, T> modified)
    {
        var before = Index(previous);
        var after = Index(current);
        foreach (var key in before.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var child = path.Append(key);
            if (!before.TryGetValue(key, out var oldValue)) added(child, after[key]);
            else if (!after.TryGetValue(key, out var newValue)) removed(child, oldValue);
            else modified(child, oldValue, newValue);
        }

        Dictionary<string, T> Index(IEnumerable<T> values)
        {
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                var key = id(value);
                if (string.IsNullOrEmpty(key) || !result.TryAdd(key, value))
                    throw new ArgumentException($"Missing or duplicate semantic identifier at '{path}': '{key}'.");
            }
            return result;
        }
    }
}
