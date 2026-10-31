using Packata.Core;
using Packata.Core.Contracts;
using Packata.Provisioners.Database;
namespace Packata.Application.Generation;
internal static class DdlGeneration
{
    internal static GenerationResult Generate(ContractDocument document, GenerationOptions options, CancellationToken token)
    {
        var mapped = ContractInspector.Inspect(document, token).Mapping;
        var diagnostics = mapped.Diagnostics.Select(d => new GenerationDiagnostic(d.Code, d.Severity.ToString(), d.SourcePath, ContractJson.Redact(d.Message))).ToList();
        GenerationResult Result(string? content) => new(content, "schema.sql", "sql", options.AssetId, null, null, diagnostics) { MediaType = "application/sql" };
        if (!mapped.IsSuccessful) return Result(null);
        var contract = mapped.RequireValue();
        var assets = contract.Assets.Where(a => a.Schema is { Fields.Count: > 0 } && (options.AssetId is null || a.Id == options.AssetId)).ToArray();
        if (assets.Length == 0 || (options.AssetId is not null && assets.Length != 1))
        {
            diagnostics.Add(new("GEN_ASSET", "Error", "$.assets", "Select an unambiguous eligible asset ID, or omit --asset to generate all eligible assets."));
            return Result(null);
        }
        foreach (var asset in assets)
            foreach (var field in asset.Schema!.Fields)
            {
                token.ThrowIfCancellationRequested();
                var type = FieldTypeResolver.Resolve(field, new(SourcePath: asset.Id + "/" + field.Name));
                diagnostics.AddRange(type.Diagnostics.Where(d => d.Target is TypeMappingTarget.Source or TypeMappingTarget.Database)
                    .Select(d => new GenerationDiagnostic(d.Code, d.Severity.ToString(), d.FieldPath, ContractJson.Redact(d.Message))));
                if (field.Children.Count > 0 || field.Items is not null)
                    diagnostics.Add(new("DDL_SHAPE", "Error", type.FieldPath, "Nested structures cannot be represented as relational columns by this planner."));
            }
        if (diagnostics.Any(d => d.Severity == "Error")) return Result(null);
        try
        {
            var plan = new RelationalSchemaPlanner(OfflineSqlDialects.Resolve(options.Dialect!)).PlanSchema(contract with { Assets = assets }, options.DdlOptions, assetId: options.AssetId, cancellationToken: token);
            diagnostics.AddRange(plan.Diagnostics.Select(d => new GenerationDiagnostic(d.Code,
                d.Code is "PROV002" or "PROV003" or "PROV008" ? "Error" : "Warning", "$.assets/" + d.AssetId, ContractJson.Redact(d.Message))));
            return Result(diagnostics.Any(d => d.Severity == "Error") ? null : plan.Script.ReplaceLineEndings("\n"));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { diagnostics.Add(new("DDL_RENDER", "Error", "$.assets", "The selected dialect cannot render this schema. Inspect physical types and supported relational constraints.")); return Result(null); }
    }
}
