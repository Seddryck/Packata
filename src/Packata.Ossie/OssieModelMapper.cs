using Packata.Core.Contracts;

namespace Packata.Ossie;

/// <summary>Projects Ossie dataset declarations into canonical assets.</summary>
public sealed class OssieModelMapper : IDataContractMapper<OssieModel>
{
    public MappingResult<DataContract> Map(OssieModel document)
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
        var assets = document.Datasets.Select((dataset, index) => MapDataset(dataset, index, diagnostics)).ToArray();
        var modelMetrics = document.Metrics ?? [];
        for (var index = 0; index < modelMetrics.Count; index++)
        {
            diagnostics.Add(new("OSSIE002", MappingSeverity.Warning, $"$/metrics/{index}",
                $"Model-scoped metric '{modelMetrics[index].Name}' has no owning dataset and remains in the Ossie model."));
        }

        return new(new DataContract(new(document.Name), new(Description: document.Description), assets, [], new()), diagnostics);
    }

    private static DataAsset MapDataset(OssieDataset dataset, int datasetIndex, ICollection<MappingDiagnostic> diagnostics)
    {
        var declarations = dataset.Fields ?? [];
        var fields = declarations.Select(field => new DataField(field.Name, MapType(field.DataType) switch
        {
            "float" => "number",
            "datetimeWithTimezone" => "timestamp",
            var type => type,
        }, Description: field.Description)).ToArray();
        var lineage = new List<FieldLineage>();
        for (var index = 0; index < declarations.Count; index++)
        {
            var field = declarations[index];
            if (field.Expression.Sql is { } sql)
                lineage.Add(new([field.Name], [new(dataset.Source, null, [])], Logic: sql));
            if (field.Expression.Dialects is { Count: > 0 })
            {
                diagnostics.Add(new("OSSIE003", MappingSeverity.Warning, $"$/datasets/{datasetIndex}/fields/{index}/expression/dialects",
                    "Dialect-specific field expressions remain in the Ossie model."));
            }
        }

        var fieldNames = fields.ToDictionary(field => OssieDeclarationValidation.Normalize(field.Name), field => field.Name,
            StringComparer.Ordinal);
        var primaryKey = (dataset.PrimaryKey ?? []).Select(name => fieldNames[OssieDeclarationValidation.Normalize(name)]).ToArray();
        var measures = (dataset.Metrics ?? []).Select(MapMeasure).ToArray();
        return new(dataset.Name, dataset.Name, null, dataset.Description, AssetKind.View,
            new DataSchema(fields, primaryKey) { Measures = measures }) { Lineage = lineage.AsReadOnly() };
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
}
