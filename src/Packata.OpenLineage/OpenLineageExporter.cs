using System.Text.Json.Nodes;
using Packata.Core.Contracts;

namespace Packata.OpenLineage;

public sealed record OpenLineageDataset(string Namespace, string Name);

public sealed record OpenLineageJob(string Namespace, string Name);

public sealed record OpenLineageExportOptions(
    OpenLineageJob Job,
    OpenLineageDataset Output,
    Uri Producer,
    DateTimeOffset EventTime,
    Func<LineageSource, OpenLineageDataset?> ResolveSource);

/// <summary>Exports declared field and dataset dependencies for one canonical asset.</summary>
public static class OpenLineageExporter
{
    private const string EventSchema = "https://openlineage.io/spec/2-0-2/OpenLineage.json#/$defs/JobEvent";
    private const string FacetSchema = "https://openlineage.io/spec/facets/1-2-0/ColumnLineageDatasetFacet.json#/$defs/ColumnLineageDatasetFacet";

    public static MappingResult<JsonObject> Export(DataAsset asset, OpenLineageExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(options);
        if (!ValidIdentity(options.Job.Namespace, options.Job.Name)
            || !ValidIdentity(options.Output.Namespace, options.Output.Name) || !options.Producer.IsAbsoluteUri)
        {
            return MappingResult<JsonObject>.Failure(new MappingDiagnostic("OL001", MappingSeverity.Error, "$",
                "OpenLineage export requires a job identity, an output dataset identity, and an absolute producer URI."));
        }

        var path = $"assets/{asset.Id}/lineage";
        if (asset.Lineage.Count == 0)
            return MappingResult<JsonObject>.Failure(new MappingDiagnostic("OL002", MappingSeverity.Error, path, "The asset declares no lineage."));

        var diagnostics = new List<MappingDiagnostic>();
        var datasets = new HashSet<OpenLineageDataset>();
        var fields = new JsonObject();
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lineage in asset.Lineage)
        {
            var target = string.Join('.', lineage.TargetPath);
            if (!targets.Add(target))
            {
                return MappingResult<JsonObject>.Failure(new MappingDiagnostic("OL004", MappingSeverity.Error, path,
                    $"Multiple lineage declarations resolve to output field '{target}'."));
            }

            var inputFields = new JsonArray();
            var edges = new HashSet<(OpenLineageDataset Dataset, string Field)>();
            foreach (var source in lineage.Sources)
            {
                var dataset = options.ResolveSource(source);
                if (dataset is null || !ValidIdentity(dataset.Namespace, dataset.Name))
                {
                    diagnostics.Add(new("OL003", MappingSeverity.Warning, path,
                        $"No OpenLineage dataset identity could be resolved for source '{source.Reference}'."));
                    continue;
                }

                datasets.Add(dataset);
                foreach (var field in source.Fields.Where(field => edges.Add((dataset, field))))
                    inputFields.Add(InputField(dataset, field, lineage));
            }

            if (inputFields.Count > 0)
                fields.Add(target, new JsonObject { ["inputFields"] = inputFields });
        }

        if (datasets.Count == 0)
        {
            diagnostics.Add(new("OL005", MappingSeverity.Error, path, "No source datasets could be exported."));
            return new(null, diagnostics);
        }

        var output = Dataset(options.Output);
        if (fields.Count > 0)
        {
            output["facets"] = new JsonObject
            {
                ["columnLineage"] = new JsonObject
                {
                    ["_producer"] = options.Producer.AbsoluteUri,
                    ["_schemaURL"] = FacetSchema,
                    ["fields"] = fields,
                },
            };
        }

        var inputs = new JsonArray();
        foreach (var dataset in datasets)
            inputs.Add(Dataset(dataset));
        var jobEvent = new JsonObject
        {
            ["eventTime"] = options.EventTime.UtcDateTime,
            ["producer"] = options.Producer.AbsoluteUri,
            ["schemaURL"] = EventSchema,
            ["job"] = new JsonObject { ["namespace"] = options.Job.Namespace, ["name"] = options.Job.Name },
            ["inputs"] = inputs,
            ["outputs"] = new JsonArray(output),
        };
        return new(jobEvent, diagnostics);
    }

    private static bool ValidIdentity(string @namespace, string name)
        => !string.IsNullOrWhiteSpace(@namespace) && !string.IsNullOrWhiteSpace(name);

    private static JsonObject Dataset(OpenLineageDataset dataset) => new()
    {
        ["namespace"] = dataset.Namespace,
        ["name"] = dataset.Name,
    };

    private static JsonObject InputField(OpenLineageDataset dataset, string field, FieldLineage lineage)
    {
        var input = Dataset(dataset);
        input["field"] = field;
        input["transformations"] = new JsonArray(new JsonObject
        {
            ["type"] = "DIRECT",
            ["subtype"] = lineage.IsIdentity ? "IDENTITY" : "TRANSFORMATION",
            ["description"] = lineage.Description ?? lineage.Logic ?? string.Empty,
            ["masking"] = false,
        });
        return input;
    }
}
