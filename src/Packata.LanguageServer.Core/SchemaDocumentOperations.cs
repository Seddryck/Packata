using System.Text;
using Packata.Application;
using Packata.Application.Generation;
using Packata.Core.Contracts;

namespace Packata.LanguageServer.Core;

public sealed record SchemaDocumentDiscovery(int SchemaVersion, string Uri, int? Version, string SelectionKey,
    IReadOnlyList<SchemaDocumentFormat> Formats, IReadOnlyList<GenerationAssetOption> Assets,
    IReadOnlyList<GenerationDiagnostic> Diagnostics);
public sealed record SchemaDocumentRequest(string Uri, string SelectionKey, Packata.Application.SchemaDocumentOptions Options,
    int? Version = null, string? SourcePath = null, int SchemaVersion = 1);
public sealed record SchemaDocumentResponse(int SchemaVersion, string Uri, int? Version, string SelectionKey,
    SchemaDocumentArtifact Result);
public sealed record SchemaDocumentArtifact(string? Content, byte[]? Bytes, string? SuggestedFilename,
    string? AssetId, string Representation, bool IsSuccessful, IReadOnlyList<GenerationDiagnostic> Diagnostics);

/// <summary>Schema documents use synchronized source snapshots; binary content is a base64 artifact on JSON transport.</summary>
public sealed class SchemaDocumentOperations(DocumentStore documents)
{
    public async Task<SchemaDocumentDiscovery> OptionsAsync(Uri uri, CancellationToken token)
    {
        var snapshot = await documents.ReadAsync(uri, token);
        var document = Parse(snapshot);
        var mapping = ContractInspector.Inspect(document, token).Mapping;
        var prefix = document.Format == "odcs" ? "/schema/" : "/resources/";
        var assets = mapping.IsSuccessful ? mapping.RequireValue().Assets.Select((a, i) =>
            new GenerationAssetOption(a.Id, ContractJson.Redact(a.Name), prefix + i, a.Schema is not null, a.Schema is null ? "No schema." : null)).ToArray() : [];
        return new(1, uri.AbsoluteUri, snapshot.Version, SchemaGenerationOperations.Key(snapshot.Text), SchemaDocumentService.Formats, assets, Findings(mapping.Diagnostics));
    }

    public async Task<SchemaDocumentResponse> ExportAsync(SchemaDocumentRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var uri = WorkspaceUri.Parse(request.Uri);
        var snapshot = await documents.ReadAsync(uri, token);
        var key = SchemaGenerationOperations.Key(snapshot.Text);
        if (key != request.SelectionKey || snapshot.Version != request.Version)
            return Failure("EXPORT_STALE", "Source changed after selection. Refresh and export again.");
        var document = Parse(snapshot);
        if (request.SourcePath is not null)
        {
            var discovery = await OptionsAsync(uri, token);
            if (!discovery.Assets.Any(a => a.Eligible && a.Id == request.Options.AssetId && a.SourcePath == request.SourcePath))
                return Failure("EXPORT_SELECTION", "Selected asset no longer matches its source.");
        }
        var result = await SchemaDocumentService.ExportAsync(document, request.Options, token);
        token.ThrowIfCancellationRequested();
        if (snapshot != await documents.ReadAsync(uri, token)) return Failure("EXPORT_STALE", "Source changed during export. Export again.");
        return new(1, uri.AbsoluteUri, snapshot.Version, key, Artifact(result));

        SchemaDocumentResponse Failure(string code, string message) => new(1, uri.AbsoluteUri, snapshot.Version, key,
            Artifact(new(null, null, null, request.Options.AssetId, "binary", [new(code, MappingSeverity.Error, "$", message)])));
    }

    private static SchemaDocumentArtifact Artifact(SchemaDocumentResult result) => new(result.Content, result.Bytes,
        result.SuggestedFilename, result.AssetId, result.Representation, result.IsSuccessful, Findings(result.Diagnostics));

    private static IReadOnlyList<GenerationDiagnostic> Findings(IReadOnlyList<MappingDiagnostic> diagnostics)
        => diagnostics.Select(d => new GenerationDiagnostic(d.Code, d.Severity.ToString(), d.SourcePath, ContractJson.Redact(d.Message))).ToArray();

    private static ContractDocument Parse(DocumentSnapshot snapshot)
    {
        if (Encoding.UTF8.GetByteCount(snapshot.Text) > ContractService.MaximumDocumentBytes) throw new FormatException("Descriptor snapshot is too large.");
        return ContractDocument.Parse(snapshot.Text, baseUri: new Uri(snapshot.Uri, "."));
    }
}
