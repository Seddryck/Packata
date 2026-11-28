namespace Packata.Ossie;

internal static class OssieDeclarationValidation
{
    internal static void Validate(OssieModel model)
    {
        var datasets = new Dictionary<string, DatasetKeys>(StringComparer.Ordinal);
        for (var index = 0; index < model.Datasets.Count; index++)
        {
            var dataset = model.Datasets[index];
            var path = $"$/datasets/{index}";
            var keys = new DatasetKeys(dataset, path);
            if (!datasets.TryAdd(Normalize(dataset.Name), keys))
                throw new OssieDocumentException(path + "/name", "Duplicate dataset name.");
        }

        CheckMetrics(model.Metrics ?? [], new HashSet<string>(StringComparer.Ordinal), "$/metrics");
        var relationships = model.Relationships ?? [];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < relationships.Count; index++)
            CheckRelationship(relationships[index], datasets, names, $"$/relationships/{index}");
    }

    private static void CheckMetrics(IReadOnlyList<OssieMetric> metrics, HashSet<string> names, string path)
    {
        for (var index = 0; index < metrics.Count; index++)
        {
            if (!names.Add(Normalize(metrics[index].Name)))
                throw new OssieDocumentException($"{path}/{index}/name", "Duplicate name in the metric's scope.");
        }
    }

    private static void CheckRelationship(OssieRelationship relationship, Dictionary<string, DatasetKeys> datasets, HashSet<string> names, string path)
    {
        if (!names.Add(Normalize(relationship.Name)))
            throw new OssieDocumentException(path + "/name", "Duplicate relationship name.");

        if (!datasets.TryGetValue(Normalize(relationship.From), out var from))
            throw new OssieDocumentException(path + "/from", "Unknown dataset.");

        if (!datasets.TryGetValue(Normalize(relationship.To), out var to))
            throw new OssieDocumentException(path + "/to", "Unknown dataset.");

        if (relationship.FromColumns.Count != relationship.ToColumns.Count)
            throw new OssieDocumentException(path + "/to_columns", "Relationship endpoints must have the same number of fields.");

        from.ResolveFields(relationship.FromColumns, path + "/from_columns");
        var target = to.ResolveFields(relationship.ToColumns, path + "/to_columns");
        if (!to.Keys.Any(key => key.SetEquals(target)))
            throw new OssieDocumentException(path + "/to_columns", "The target fields must match a primary or unique key.");
    }

    internal static string Normalize(string identifier)
        => identifier.Length >= 2 && identifier[0] == '"' && identifier[^1] == '"'
            ? identifier[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal)
            : identifier.ToUpperInvariant();

    private sealed class DatasetKeys
    {
        private readonly HashSet<string> fields = new(StringComparer.Ordinal);

        internal DatasetKeys(OssieDataset dataset, string path)
        {
            var declarations = dataset.Fields ?? [];
            for (var index = 0; index < declarations.Count; index++)
            {
                if (!fields.Add(Normalize(declarations[index].Name)))
                    throw new OssieDocumentException($"{path}/fields/{index}/name", "Duplicate field name.");
            }

            CheckMetrics(dataset.Metrics ?? [], new HashSet<string>(fields, StringComparer.Ordinal), path + "/metrics");
            if (dataset.PrimaryKey is { } primaryKey)
                Keys.Add(ResolveFields(primaryKey, path + "/primary_key"));

            var uniqueKeys = dataset.UniqueKeys ?? [];
            for (var index = 0; index < uniqueKeys.Count; index++)
                Keys.Add(ResolveFields(uniqueKeys[index], $"{path}/unique_keys/{index}"));
        }

        internal List<HashSet<string>> Keys { get; } = [];

        internal HashSet<string> ResolveFields(IReadOnlyList<string> references, string path)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < references.Count; index++)
            {
                var field = Normalize(references[index]);
                if (!fields.Contains(field))
                    throw new OssieDocumentException($"{path}/{index}", "Unknown field.");

                if (!result.Add(field))
                    throw new OssieDocumentException($"{path}/{index}", "The field is listed more than once.");
            }

            return result;
        }
    }
}
