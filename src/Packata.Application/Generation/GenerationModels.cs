using Packata.Core;

namespace Packata.Application.Generation;

public sealed record GenerationOptions(string? AssetId = null, string? Target = null,
    string? Engine = null, string? Profile = null, string? Namespace = null, string? TypeName = null,
    IReadOnlyList<string>? Defines = null, TypeMappingFallback Fallback = TypeMappingFallback.Reject,
    string? Language = null, string Operation = "generate", string? OutputFormat = null,
    string? Dialect = null, string? CompatibilityProfile = null, Packata.Core.Provisioning.ContractProvisioningOptions? DdlOptions = null);
public sealed record TemplateInput(string Identity, string Text, int? Version = null);
public sealed record GenerationDiagnostic(string Code, string Severity, string SourcePath, string Message,
    string? Target = null, string? Template = null, int? Line = null, int? Column = null);
public sealed record GenerationResult(string? Content, string? SuggestedFilename, string? Language,
    string? AssetId, string? TemplateIdentity, int? TemplateVersion,
    IReadOnlyList<GenerationDiagnostic> Diagnostics, IReadOnlyDictionary<string, object?>? Context = null)
{
    public string Representation { get; init; } = "text";
    public byte[]? BinaryContent { get; init; }
    public string MediaType { get; init; } = "text/plain";
    public bool IsSuccessful => (Content is not null || BinaryContent is not null) && Diagnostics.All(d => d.Severity != "Error");
}
public sealed record GenerationTarget(string Id, string Label, string Profile, string Engine,
    string Language, bool SupportsCSharpOptions, string Limitation,
    string Operation = "generate", string Category = "schema-code", string Representation = "C#",
    string Extension = ".cs", IReadOnlyList<string>? ApplicableOptions = null,
    IReadOnlyList<string>? CompatibilityProfiles = null, IReadOnlyList<SchemaDialect>? Dialects = null, bool WholeContract = false)
{
    public IReadOnlyList<string> Options => ApplicableOptions ?? (Operation == "export" ? ["asset", "output", "profile"] : ["asset", "output", "namespace", "type-name"]);
    public string Invocation => Id == "ddl" ? "schema generate <SOURCE> --target ddl --dialect <dialect>" : Operation == "export" ? $"schema export <SOURCE> --output-format {Id}" : $"schema generate <SOURCE> --target {Id} --language {Language}";
}
public sealed record SchemaDialect(string Id, string Label);
public sealed record GenerationCatalog(int SchemaVersion, int ContextVersion,
    IReadOnlyList<GenerationTarget> Targets, IReadOnlyList<string> Profiles,
    IReadOnlyList<string> Engines, string DefaultEngine, IReadOnlyList<string> Limitations);
public sealed record GenerationAsset(string Id, string Name, int Index, bool Eligible, string? Reason = null);
public sealed record GenerationDiscovery(GenerationCatalog Catalog, IReadOnlyList<GenerationAsset> Assets,
    IReadOnlyList<GenerationDiagnostic> Diagnostics);
