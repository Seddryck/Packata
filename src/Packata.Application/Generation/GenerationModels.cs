using Packata.Core;

namespace Packata.Application.Generation;

public sealed record GenerationOptions(string? AssetId = null, string? Target = null,
    string? Engine = null, string? Profile = null, string? Namespace = null, string? TypeName = null,
    IReadOnlyList<string>? Defines = null, TypeMappingFallback Fallback = TypeMappingFallback.Reject);
public sealed record TemplateInput(string Identity, string Text, int? Version = null);
public sealed record GenerationDiagnostic(string Code, string Severity, string SourcePath, string Message,
    string? Target = null, string? Template = null, int? Line = null, int? Column = null);
public sealed record GenerationResult(string? Content, string? SuggestedFilename, string? Language,
    string? AssetId, string? TemplateIdentity, int? TemplateVersion,
    IReadOnlyList<GenerationDiagnostic> Diagnostics, IReadOnlyDictionary<string, object?>? Context = null)
{
    public bool IsSuccessful => Content is not null && Diagnostics.All(d => d.Severity != "Error");
}
public sealed record GenerationTarget(string Id, string Label, string Profile, string Engine,
    string Language, bool SupportsCSharpOptions, string Limitation);
public sealed record GenerationCatalog(int SchemaVersion, int ContextVersion,
    IReadOnlyList<GenerationTarget> Targets, IReadOnlyList<string> Profiles,
    IReadOnlyList<string> Engines, string DefaultEngine, IReadOnlyList<string> Limitations);
public sealed record GenerationAsset(string Id, string Name, int Index, bool Eligible, string? Reason = null);
public sealed record GenerationDiscovery(GenerationCatalog Catalog, IReadOnlyList<GenerationAsset> Assets,
    IReadOnlyList<GenerationDiagnostic> Diagnostics);
