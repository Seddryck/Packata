using Packata.Core.Contracts;

namespace Packata.Ossie;

/// <summary>Projects published Ossie dataset declarations into canonical assets.</summary>
public sealed class OssieModelMapper : IDataContractMapper<OssieModel>
{
    public MappingResult<DataContract> Map(OssieModel document) => Map(document, null);

    /// <summary>Maps selected datasets. A null selection publishes all declarations.</summary>
    public MappingResult<DataContract> Map(OssieModel document, IReadOnlyList<OssieDatasetPublication>? publications)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            OssieModelSerializer.ValidateModel(document);
        }
        catch (OssieDocumentException exception)
        {
            return MappingResult<DataContract>.Failure(new MappingDiagnostic("OSSIE001", MappingSeverity.Error,
                exception.Path, exception.Message[(exception.Path.Length + 2)..]));
        }

        var diagnostics = new List<MappingDiagnostic>();
        var datasets = document.Datasets.Select((dataset, index) => new DatasetDeclaration(dataset, index))
            .ToDictionary(item => OssieDeclarationValidation.Normalize(item.Dataset.Name), StringComparer.Ordinal);
        var selection = publications ?? document.Datasets.Select(dataset => new OssieDatasetPublication(dataset.Name)).ToArray();
        var assets = MapPublishedDatasets(selection, datasets, diagnostics);
        MapRelationships(document.Relationships ?? [], datasets, assets, diagnostics);
        ReportModelMetrics(document.Metrics ?? [], diagnostics);

        return new(new DataContract(new(document.Name), new(Description: document.Description), assets.Values.ToArray(), [], new()), diagnostics);
    }

    private static Dictionary<string, DataAsset> MapPublishedDatasets(IReadOnlyList<OssieDatasetPublication> selection,
        IReadOnlyDictionary<string, DatasetDeclaration> datasets, ICollection<MappingDiagnostic> diagnostics)
    {
        var assets = new Dictionary<string, DataAsset>(StringComparer.Ordinal);
        for (var index = 0; index < selection.Count; index++)
        {
            var publication = selection[index];
            var key = OssieDeclarationValidation.Normalize(publication.DatasetName);
            var path = $"$/publication/{index}/dataset";
            if (!datasets.TryGetValue(key, out var declaration))
                diagnostics.Add(new("OSSIE004", MappingSeverity.Error, path, $"Unknown dataset '{publication.DatasetName}'."));
            else if (assets.ContainsKey(key))
                diagnostics.Add(new("OSSIE004", MappingSeverity.Error, path, $"Dataset '{publication.DatasetName}' is selected more than once."));
            else
                assets.Add(key, MapDataset(declaration, publication.PhysicalName, diagnostics));
        }

        return assets;
    }

    private static DataAsset MapDataset(DatasetDeclaration declaration, string? physicalName, ICollection<MappingDiagnostic> diagnostics)
    {
        var (dataset, datasetIndex) = declaration;
        var fieldDeclarations = dataset.Fields ?? [];
        var fields = fieldDeclarations.Select(MapField).ToArray();
        var lineage = fieldDeclarations.Select((field, index) =>
            OssieFieldLineage.Map(field, dataset.Source, $"$/datasets/{datasetIndex}/fields/{index}/expression", diagnostics)).ToArray();
        var primaryKey = declaration.ResolveFields(dataset.PrimaryKey ?? []);
        var uniqueKeys = (dataset.UniqueKeys ?? []).Select(declaration.ResolveFields).ToArray();
        var measures = (dataset.Metrics ?? []).Select(MapMeasure).ToArray();
        return new(dataset.Name, dataset.Name, physicalName, dataset.Description, AssetKind.View,
            new DataSchema(fields, primaryKey) { Measures = measures, UniqueKeys = uniqueKeys }) { Lineage = lineage };
    }

    private static DataField MapField(OssieField field)
    {
        var constraints = field.DataType switch
        {
            OssieDataType.DateTime => new DataConstraint[] { new("timezone", false) },
            OssieDataType.DateTimeTz => [new("timezone", true)],
            _ => [],
        };
        var type = MapType(field.DataType) switch
        {
            "float" => "number",
            "datetime" or "datetimeWithTimezone" => "timestamp",
            var canonical => canonical,
        };
        return new(field.Name, type, Constraints: constraints, Description: field.Description) { IsNullabilityKnown = false };
    }

    private static void MapRelationships(IReadOnlyList<OssieRelationship> relationships,
        IReadOnlyDictionary<string, DatasetDeclaration> datasets, Dictionary<string, DataAsset> assets,
        ICollection<MappingDiagnostic> diagnostics)
    {
        for (var index = 0; index < relationships.Count; index++)
        {
            var relationship = relationships[index];
            var from = OssieDeclarationValidation.Normalize(relationship.From);
            var to = OssieDeclarationValidation.Normalize(relationship.To);
            var sourcePublished = assets.TryGetValue(from, out var source);
            var targetPublished = assets.TryGetValue(to, out var target);
            if (sourcePublished != targetPublished)
            {
                diagnostics.Add(new("OSSIE005", MappingSeverity.Warning, $"$/relationships/{index}",
                    $"Relationship '{relationship.Name}' has an endpoint outside the publication selection."));
            }
            else if (source is not null && target is not null)
            {
                var mapped = new DataRelationship(datasets[from].ResolveFields(relationship.FromColumns),
                    target.Id, datasets[to].ResolveFields(relationship.ToColumns), relationship.Name);
                var schema = source.Schema!;
                assets[from] = source with { Schema = schema with { Relationships = [.. schema.Relationships, mapped] } };
            }
        }
    }

    private static void ReportModelMetrics(IReadOnlyList<OssieMetric> metrics, ICollection<MappingDiagnostic> diagnostics)
    {
        for (var index = 0; index < metrics.Count; index++)
        {
            diagnostics.Add(new("OSSIE002", MappingSeverity.Warning, $"$/metrics/{index}",
                $"Model-scoped metric '{metrics[index].Name}' has no owning dataset and remains in the Ossie model."));
        }
    }

    private static DataMeasure MapMeasure(OssieMetric metric)
    {
        var dialects = (metric.Expression.Dialects ?? []).Select(expression => new DataMeasureExpression(expression.Expression, expression.Dialect));
        return new(metric.Name, metric.Description, MapType(metric.DataType))
        {
            Expressions = metric.Expression.Sql is { } sql ? [new(sql), .. dialects] : [.. dialects],
        };
    }

    private static string? MapType(OssieDataType? type) => type switch
    {
        OssieDataType.String => "string",
        OssieDataType.Integer => "integer",
        OssieDataType.Decimal => "decimal",
        OssieDataType.Float => "float",
        OssieDataType.Boolean => "boolean",
        OssieDataType.Date => "date",
        OssieDataType.Time => "time",
        OssieDataType.DateTime => "datetime",
        OssieDataType.DateTimeTz => "datetimeWithTimezone",
        OssieDataType.Opaque => "opaque",
        _ => null,
    };

    private sealed record DatasetDeclaration(OssieDataset Dataset, int Index)
    {
        private readonly Dictionary<string, string> fieldNames = (Dataset.Fields ?? [])
            .ToDictionary(field => OssieDeclarationValidation.Normalize(field.Name), field => field.Name, StringComparer.Ordinal);

        internal IReadOnlyList<string> ResolveFields(IReadOnlyList<string> names)
            => names.Select(name => fieldNames[OssieDeclarationValidation.Normalize(name)]).ToArray();
    }
}
