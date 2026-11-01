using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Provisioning;
using Packata.Provisioners.Database;
namespace Packata.Application.Generation;
internal static class DdlGeneration
{
    internal static async Task<GenerationResult> GenerateAsync(ContractDocument document, GenerationOptions options, CancellationToken token)
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
        return await GenerateRelationalAsync(document, contract, assets, options, diagnostics, token).ConfigureAwait(false);
    }

    private static async Task<GenerationResult> GenerateRelationalAsync(ContractDocument document, DataContract contract,
        DataAsset[] assets, GenerationOptions options, List<GenerationDiagnostic> diagnostics, CancellationToken token)
    {
        GenerationResult Result() => new(null, "schema.sql", "sql", options.AssetId, null, null, diagnostics) { MediaType = "application/sql" };
        DataAsset[] references = [];
        var planner = new RelationalSchemaPlanner(OfflineSqlDialects.Resolve(options.Dialect!));
        var capabilityErrors = planner.ValidateCapabilities(assets, options.DdlOptions).Where(d => d.Code == "PROV009").ToArray();
        diagnostics.AddRange(capabilityErrors.Select(Diagnostic));
        if (capabilityErrors.Length > 0) return Result();
        if ((options.DdlOptions ?? new()).Constraints.HasFlag(ContractConstraintOptions.ForeignKeys))
        {
            var resolution = await new DdlReferences(document, contract, assets, diagnostics, token).ResolveAsync().ConfigureAwait(false);
            contract = resolution.Contract;
            assets = resolution.Assets;
            references = resolution.References;
        }
        ValidateAssets(assets, diagnostics, token);
        if (diagnostics.Any(d => d.Severity == "Error")) return Result();
        return Render(contract, assets, references, options, diagnostics, token);
    }

    private static void ValidateAssets(IEnumerable<DataAsset> assets, List<GenerationDiagnostic> diagnostics, CancellationToken token)
    {
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
    }

    private static GenerationResult Render(DataContract contract, DataAsset[] assets, DataAsset[] references, GenerationOptions options,
        List<GenerationDiagnostic> diagnostics, CancellationToken token)
    {
        GenerationResult Result(string? content) => new(content, "schema.sql", "sql", options.AssetId, null, null, diagnostics) { MediaType = "application/sql" };
        try
        {
            var plan = new RelationalSchemaPlanner(OfflineSqlDialects.Resolve(options.Dialect!)).PlanSchema(contract with { Assets = assets }, options.DdlOptions, assetId: options.AssetId, cancellationToken: token, referencedAssets: references);
            diagnostics.AddRange(plan.Diagnostics.Select(Diagnostic));
            return Result(diagnostics.Any(d => d.Severity == "Error") ? null : plan.Script.ReplaceLineEndings("\n"));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { diagnostics.Add(new("DDL_RENDER", "Error", "$.assets", "The selected dialect cannot render this schema. Inspect physical types and supported relational constraints.")); return Result(null); }
    }

    private static GenerationDiagnostic Diagnostic(ProvisioningDiagnostic diagnostic) => new(diagnostic.Code,
        diagnostic.Code switch { "PROV002" or "PROV003" or "PROV008" or "PROV009" => "Error", "PROV010" or "PROV012" => "Information", _ => "Warning" },
        "$.assets/" + diagnostic.AssetId, ContractJson.Redact(diagnostic.Message));
}
