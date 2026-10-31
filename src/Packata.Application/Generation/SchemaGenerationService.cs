using System.Text.RegularExpressions;
using Packata.Core.Contracts;

namespace Packata.Application.Generation;

/// <summary>Metadata-only generation workflow shared by command-line and editor hosts.</summary>
public sealed class SchemaGenerationService(ISchemaTemplateRenderer? renderer = null)
{
    public static GenerationCatalog Catalog => GenerationCatalogService.Catalog;

    public static GenerationDiscovery Discover(ContractDocument document, CancellationToken cancellationToken = default)
    {
        var inspection = ContractInspector.Inspect(document, cancellationToken);
        var diagnostics = inspection.Mapping.Diagnostics.Select(d => new GenerationDiagnostic(d.Code,
            d.Severity.ToString(), d.SourcePath, ContractJson.Redact(d.Message))).ToArray();
        var assets = inspection.Mapping.Value?.Assets.Select((asset, index) => new GenerationAsset(asset.Id,
            ContractJson.Redact(asset.Name), index, inspection.Mapping.IsSuccessful && asset.Schema is { Fields.Count: > 0 },
            asset.Schema is { Fields.Count: > 0 } ? null : "The asset has no usable schema.")).ToArray() ?? [];
        return new(Catalog, assets, diagnostics);
    }

    public static string? ValidateOptions(GenerationOptions options, TemplateInput? template)
    {
        try
        {
            ValidateSelection(options, template);
            if (options.Operation == "generate" && options.Target != "ddl") SelectTemplate(options, template);
            GenerationData.Variables(options.Defines);
            CSharpNames.Namespace(options.Namespace);
            return null;
        }
        catch (ArgumentException error) { return ContractJson.Redact(error.Message); }
    }

    public async Task<GenerationResult> GenerateAsync(string descriptor, GenerationOptions options,
        TemplateInput? template = null, string format = "auto", Uri? baseUri = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await GenerateAsync(ContractDocument.Parse(descriptor, format, baseUri), options, template, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return Failure("GEN_DOCUMENT", "Invalid or unsupported descriptor. Check its syntax and declared format.");
        }
    }

    public async Task<GenerationResult> GenerateAsync(ContractDocument document, GenerationOptions options,
        TemplateInput? template = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try {
            var error = ValidateOptions(options, template);
            if (error is not null) return Failure("GEN_OPTIONS", error);
            if (options.Operation == "export") return ExportDocument(document, options, cancellationToken);
            if (options.Target == "ddl") return DdlGeneration.Generate(document, options, cancellationToken);
            return await GenerateDocumentAsync(document, options, template, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Failure("GEN_DOCUMENT", "The descriptor cannot be prepared for schema generation. Inspect its mapping and type declarations."); }
    }

    private async Task<GenerationResult> GenerateDocumentAsync(ContractDocument document, GenerationOptions options,
        TemplateInput? template, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? profile;
        string engine;
        try
        {
            (template, profile, engine) = SelectTemplate(options, template);
            GenerationData.Variables(options.Defines);
            CSharpNames.Namespace(options.Namespace);
        }
        catch (ArgumentException error) { return Failure("GEN_OPTIONS", ContractJson.Redact(error.Message)); }
        var inspection = ContractInspector.Inspect(document, cancellationToken);
        var diagnostics = inspection.Mapping.Diagnostics.Select(d =>
            new GenerationDiagnostic(d.Code, d.Severity.ToString(), d.SourcePath, ContractJson.Redact(d.Message))).ToList();
        if (!inspection.Mapping.IsSuccessful) return Result(null, null, null, template, diagnostics);
        var contract = inspection.Mapping.RequireValue();
        var assets = contract.Assets.Where(a => a.Schema is { Fields.Count: > 0 }).ToArray();
        var asset = SelectAsset(assets, options.AssetId, diagnostics);
        if (asset is null) return Result(null, null, null, template, diagnostics);
        var context = new GenerationProjection(options, profile, diagnostics, cancellationToken).Build(contract, asset);
        var plain = (IReadOnlyDictionary<string, object?>)GenerationData.Plain(context)!;
        if (diagnostics.Any(d => d.Severity == "Error")) return Result(null, asset.Id, null, template, diagnostics, plain);
        try
        {
            var content = await (renderer ?? new DidotTemplateRenderer()).RenderAsync(template, engine, plain, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var name = ((Dictionary<string, object?>)context["asset"]!)["generated_name"]!.ToString()!.TrimStart('@');
            var language = options.Language ?? (profile is null ? "plaintext" : "csharp");
            return Result(content.ReplaceLineEndings("\n"), asset.Id, name + (language == "csharp" ? ".cs" : ".txt"), template, diagnostics, plain, language);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            diagnostics.Add(TemplateFailure(error, template));
            return Result(null, asset.Id, null, template, diagnostics, plain);
        }
    }

    private static (TemplateInput Template, string? Profile, string Engine) SelectTemplate(GenerationOptions options, TemplateInput? template)
    {
        if (!Enum.IsDefined(options.Fallback)) throw new ArgumentException("Unknown mapping fallback policy.");
        if ((options.Target is null) == (template is null)) throw new ArgumentException("Select exactly one built-in target or custom template.");
        var target = options.Target is null ? null : Catalog.Targets.SingleOrDefault(t => t.Operation == "generate" && t.Id == options.Target && t.Language == options.Language)
            ?? throw new ArgumentException("Unsupported target/language pair. Use schema generate --target record|datatable|polars --language csharp.");
        var profile = SelectProfile(options, target);
        CheckEngine(options, target);
        template ??= GenerationCatalogService.BuiltIn(target!.Id);
        return (template, profile, GenerationCatalogService.Engine(template, target?.Engine ?? options.Engine));
    }

    private static string? SelectProfile(GenerationOptions options, GenerationTarget? target)
    {
        if (target is not null && options.Profile is not null && options.Profile != target.Profile)
            throw new ArgumentException("A built-in target fixes its mapping profile; remove the conflicting profile.");
        var profile = target?.Profile ?? options.Profile;
        if (profile is not null && !Catalog.Profiles.Contains(profile)) throw new ArgumentException("Unknown type-mapping profile.");
        return profile;
    }

    private static void CheckEngine(GenerationOptions options, GenerationTarget? target)
    {
        if (target is not null && options.Engine is not null && options.Engine != target.Engine)
            throw new ArgumentException("A built-in target fixes its template engine; engine overrides apply to custom templates.");
    }

    private static void ValidateSelection(GenerationOptions options, TemplateInput? template)
    {
        if (options.AssetId is not null && string.IsNullOrWhiteSpace(options.AssetId)) throw new ArgumentException("--asset must be a non-empty canonical ID.");
        if (options.Operation == "export")
        {
            if (options.Target is not null || options.Language is not null || options.Dialect is not null || template is not null
                || options.Engine is not null || options.Profile is not null || options.Namespace is not null || options.TypeName is not null
                || options.Defines?.Count > 0 || options.Fallback != Packata.Core.TypeMappingFallback.Reject || options.DdlOptions is not null)
                throw new ArgumentException("Export accepts --output-format and optional --profile; use schema generate for code and templates.");
            var target = Catalog.Targets.SingleOrDefault(t => t.Operation == "export" && t.Id == options.OutputFormat)
                ?? throw new ArgumentException("Unknown or missing --output-format. Discover available schema export formats.");
            if (options.DocumentOptions is not null && options.OutputFormat != "avro") throw new ArgumentException("Schema naming options apply only to Avro export.");
            if (options.CompatibilityProfile is not null && target.CompatibilityProfiles?.Contains(options.CompatibilityProfile) != true)
                throw new ArgumentException("Unknown or incompatible export --profile. Omit --profile for generic output.");
        }
        else if (options.Operation == "generate")
        {
            if (options.OutputFormat is not null || options.CompatibilityProfile is not null || options.DocumentOptions is not null)
                throw new ArgumentException("Use schema export --output-format for schema documents; --profile is export-only.");
            if (options.Target == "ddl")
            {
                if (template is not null || options.Engine is not null || options.Profile is not null || options.Namespace is not null || options.TypeName is not null || options.Defines?.Count > 0 || options.Fallback != Packata.Core.TypeMappingFallback.Reject)
                    throw new ArgumentException("DDL does not accept templates or code-generation options.");
                if (options.Language is not null && options.Language != "sql") throw new ArgumentException("DDL requires SQL; omit --language or supply --language sql.");
                if (options.Dialect is null) throw new ArgumentException("DDL requires --dialect. Use an ID from the schema catalog.");
                Packata.Provisioners.Database.OfflineSqlDialects.Resolve(options.Dialect);
                return;
            }
            if (options.DdlOptions is not null) throw new ArgumentException("Relational options apply only to target ddl.");
            if (options.Dialect is not null) throw new ArgumentException("--dialect applies only to target ddl.");
            if (options.Target is not null && options.Language is null)
                throw new ArgumentException("Bundled generation requires --target and --language (for example --target record --language csharp).");
        }
        else throw new ArgumentException("Unknown schema operation. Use export or generate.");
    }

    private static GenerationResult ExportDocument(ContractDocument document, GenerationOptions options, CancellationToken token)
    {
        var inspection = ContractInspector.Inspect(document, token);
        var diagnostics = inspection.Mapping.Diagnostics.Select(d => new GenerationDiagnostic(d.Code, d.Severity.ToString(), d.SourcePath, ContractJson.Redact(d.Message))).ToList();
        if (!inspection.Mapping.IsSuccessful) return new(null, null, null, null, null, null, diagnostics);
        if (options.AssetId is not null && inspection.Mapping.RequireValue().Assets.Count(a => a.Id == options.AssetId) > 1) return Failure("GEN_ASSET", "The selected asset ID is ambiguous.");
        var asset = SelectAsset(inspection.Mapping.RequireValue().Assets.Where(a => a.Schema is { Fields.Count: > 0 }).ToArray(), options.AssetId, diagnostics);
        if (asset is null) return new(null, null, null, null, null, null, diagnostics);
        if (options.OutputFormat is "avro" or "spark-json") return new StructuredSchemaExport(options.OutputFormat, diagnostics, token, options.DocumentOptions).Export(asset);
        try
        {
            var result = ContractExportService.JsonSchema(document, asset.Id, token);
            return new(result.Content, "schema.schema.json", "json", asset.Id, null, null, diagnostics) { MediaType = "application/schema+json" };
        }
        catch (JsonSchemaConversionException error) { return Failure("GEN_SCHEMA", ContractJson.Redact(error.Message)); }
    }

    private static DataAsset? SelectAsset(DataAsset[] assets, string? id, List<GenerationDiagnostic> diagnostics)
    {
        if (id is not null)
        {
            var matches = assets.Where(a => a.Id == id).ToArray();
            if (matches.Length > 1) { diagnostics.Add(new("GEN_ASSET", "Error", "$.assets", "The selected asset ID is ambiguous.")); return null; }
            var asset = matches.SingleOrDefault();
            if (asset is not null) return asset;
            diagnostics.Add(new("GEN_ASSET", "Error", "$.assets", "The selected asset does not exist or has no usable schema."));
        }
        else if (assets.Length == 1) return assets[0];
        else diagnostics.Add(new("GEN_ASSET", "Error", "$.assets", assets.Length == 0
            ? "No asset has an eligible schema." : "Select --asset explicitly. Eligible asset IDs: " + string.Join(", ", assets.Select(a => ContractJson.Redact(a.Id)))));
        return null;
    }

    private static GenerationDiagnostic TemplateFailure(Exception error, TemplateInput template)
    {
        var location = Regex.Match(error.Message, @"\((\d+),\s*(\d+)\)", RegexOptions.None, TimeSpan.FromSeconds(1));
        var message = ContractJson.Redact(error.Message);
        if (message.Length > 2000) message = message[..2000];
        return new("GEN_TEMPLATE", "Error", "$template", message, Template: template.Identity,
            Line: location.Success ? int.Parse(location.Groups[1].Value) : null,
            Column: location.Success ? int.Parse(location.Groups[2].Value) : null);
    }

    private static GenerationResult Failure(string code, string message)
        => new(null, null, null, null, null, null, [new(code, "Error", "$", message)]);

    private static GenerationResult Result(string? content, string? asset, string? filename, TemplateInput template,
        IReadOnlyList<GenerationDiagnostic> diagnostics, IReadOnlyDictionary<string, object?>? context = null, string? language = null)
        => new(content, filename, language, asset, template.Identity, template.Version, diagnostics, context);
}
