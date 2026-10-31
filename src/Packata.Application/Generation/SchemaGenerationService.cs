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
            SelectTemplate(options, template);
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
        try { return await GenerateDocumentAsync(document, options, template, cancellationToken).ConfigureAwait(false); }
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
        return await RenderAsync(context, asset, profile, template, engine, diagnostics, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GenerationResult> RenderAsync(Dictionary<string, object?> context, DataAsset asset,
        string? profile, TemplateInput template, string engine, List<GenerationDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        if (profile is "csharp-arrow" or "python-arrow")
            ArrowCodeProjection.Apply(context, asset.Schema!, profile == "python-arrow", diagnostics);
        var plain = (IReadOnlyDictionary<string, object?>)GenerationData.Plain(context)!;
        if (diagnostics.Any(d => d.Severity == "Error")) return Result(null, asset.Id, null, template, diagnostics, plain);
        try
        {
            var content = await (renderer ?? new DidotTemplateRenderer()).RenderAsync(template, engine, plain, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var name = ((Dictionary<string, object?>)context["asset"]!)["generated_name"]!.ToString()!.TrimStart('@');
            var (language, extension) = OutputKind(profile);
            return Result(content.ReplaceLineEndings("\n"), asset.Id, name + extension, template, diagnostics, plain, language);
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
        ValidateSelection(options, template);
        var target = SelectTarget(options);
        var profile = SelectProfile(options, target);
        if (profile == "python-arrow" && options.Namespace is not null) throw new ArgumentException("Python Arrow generation does not support --namespace.");
        CheckEngine(options, target);
        template ??= GenerationCatalogService.BuiltIn(target!.Id == "arrow" ? target.Profile : target.Id);
        return (template, profile, GenerationCatalogService.Engine(template, target?.Engine ?? options.Engine));
    }

    private static void ValidateSelection(GenerationOptions options, TemplateInput? template)
    {
        if (options.UnsupportedOptions is { Count: > 0 }) throw new ArgumentException("Unsupported generation options; use the generation catalog.");
        if (!Enum.IsDefined(options.Fallback)) throw new ArgumentException("Unknown mapping fallback policy.");
        if ((options.Target is null) == (template is null)) throw new ArgumentException("Select exactly one built-in target or custom template.");
        if (options.Target == "arrow" && options.Language is null) throw new ArgumentException("--target arrow requires --language csharp or python.");
        if (template is not null && options.Language is not null) throw new ArgumentException("Custom templates select --type-mapping instead of --language.");
    }

    private static GenerationTarget? SelectTarget(GenerationOptions options)
    {
        if (options.Target is null) return null;
        var language = options.Language ?? "csharp";
        return Catalog.Targets.SingleOrDefault(t => t.Id == options.Target && t.Language == language)
            ?? throw new ArgumentException("Unsupported target/language pair. Consult the generation catalog.");
    }

    private static (string Language, string Extension) OutputKind(string? profile) => profile switch
    {
        null => ("plaintext", ".txt"), "python-arrow" => ("python", ".py"), _ => ("csharp", ".cs")
    };

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

    private static DataAsset? SelectAsset(DataAsset[] assets, string? id, List<GenerationDiagnostic> diagnostics)
    {
        if (id is not null)
        {
            var asset = assets.SingleOrDefault(a => a.Id == id);
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
