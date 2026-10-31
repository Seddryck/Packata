using Packata.Arrow;
using Packata.Core.Contracts;

namespace Packata.Application;

public sealed record SchemaDocumentFormat(string Id, string Label, string Representation, string Extension,
    IReadOnlyList<string> Profiles);
public sealed record SchemaDocumentOptions(string OutputFormat, string? AssetId = null, string? Profile = null)
{
    [Newtonsoft.Json.JsonExtensionData(ReadData = true, WriteData = false)]
    public IDictionary<string, Newtonsoft.Json.Linq.JToken>? UnsupportedOptions { get; init; }
}
public sealed record SchemaDocumentResult(string? Content, byte[]? Bytes, string? SuggestedFilename,
    string? AssetId, string Representation, IReadOnlyList<MappingDiagnostic> Diagnostics)
{
    public bool IsSuccessful => (Content is not null || Bytes is not null) && Diagnostics.All(d => d.Severity != MappingSeverity.Error);
}

/// <summary>Metadata-only portable schema documents. Hosts publish completed text or exact binary bytes.</summary>
public static class SchemaDocumentService
{
    public static IReadOnlyList<SchemaDocumentFormat> Formats { get; } = Array.AsReadOnly(new SchemaDocumentFormat[]
    {
        new("json-schema", "JSON Schema", "text", ".schema.json", []),
        new("arrow-ipc", "Arrow IPC", "binary", ".schema.ipc", [])
    });

    public static string? ValidateOptions(SchemaDocumentOptions options)
        => options.UnsupportedOptions is { Count: > 0 } ? "Unsupported schema export options; code targets and languages belong to generation."
            : Formats.SingleOrDefault(f => f.Id == options.OutputFormat) is not { } format ? "Unsupported schema output format."
            : options.Profile is not null && !format.Profiles.Contains(options.Profile) ? "This format does not advertise the requested profile; omit --profile for generic output." : null;

    public static async Task<SchemaDocumentResult> ExportAsync(ContractDocument document, SchemaDocumentOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ValidateOptions(options) is { } error) return Failure("EXPORT_OPTIONS", error);
        var mapping = ContractInspector.Inspect(document, cancellationToken).Mapping;
        if (!mapping.IsSuccessful) return new(null, null, null, null, "binary", mapping.Diagnostics);
        var assets = mapping.RequireValue().Assets.Where(a => a.Schema is not null).ToArray();
        var matches = assets.Where(a => options.AssetId is null || a.Id == options.AssetId).ToArray();
        if (matches.Length != 1) return Failure("EXPORT_ASSET", "Select one eligible asset using --asset; absent, ambiguous or unknown schemas cannot be exported.");
        var asset = matches[0];
        var filename = Generation.CSharpNames.Identifier(asset.Name).TrimStart('@') + Formats.Single(f => f.Id == options.OutputFormat).Extension;
        if (options.OutputFormat == "json-schema")
        {
            var result = ContractExportService.JsonSchema(document, asset.Id, cancellationToken);
            return new(result.Content, null, filename, asset.Id, "text", result.Diagnostics);
        }
        try
        {
            var arrow = ArrowSchemaMapper.Map(asset.Schema!);
            var diagnostics = mapping.Diagnostics.Concat(arrow.Diagnostics.Select(d => new MappingDiagnostic(d.Code,
                d.IsError ? MappingSeverity.Error : MappingSeverity.Warning, d.Path, d.Message))).ToArray();
            if (arrow.Schema is null) return new(null, null, filename, asset.Id, "binary", diagnostics);
            var bytes = await ArrowSchemaSerializer.SerializeAsync(arrow.Schema, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(null, bytes, filename, asset.Id, "binary", diagnostics);
        }
        catch (NotSupportedException e) { return Failure("ARROW_METADATA", e.Message); }
    }

    private static SchemaDocumentResult Failure(string code, string message)
        => new(null, null, null, null, "binary", [new(code, MappingSeverity.Error, "$", message)]);
}
