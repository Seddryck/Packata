using System.Security.Cryptography;
using System.Text;
using Packata.Application;
using Packata.Application.Generation;

namespace Packata.LanguageServer.Core;

public sealed record GenerationAssetOption(string Id, string Name, string SourcePath, bool Eligible, string? Reason);
public sealed record GenerationOptionsResult(int SchemaVersion, string Uri, int? Version, string SelectionKey,
    GenerationCatalog Catalog, IReadOnlyList<GenerationAssetOption> Assets, IReadOnlyList<GenerationDiagnostic> Diagnostics);
public sealed record GenerationTemplateSnapshot(string Uri, string? Text = null, int? Version = null);
public sealed record SchemaGenerationRequest(string Uri, string SelectionKey, GenerationOptions Options,
    int? Version = null, string? SourcePath = null, GenerationTemplateSnapshot? Template = null, int SchemaVersion = 1);
public sealed record GenerationFinding(GenerationDiagnostic Diagnostic, SourceLocation? Location);
public sealed record SchemaGenerationResponse(int SchemaVersion, string Uri, int? Version, string SelectionKey,
    GenerationResult Result, IReadOnlyList<GenerationFinding> Findings);

/// <summary>Snapshot and transport boundary; all selection, projection and rendering remain in Application.</summary>
public sealed class SchemaGenerationOperations(DocumentStore documents, SchemaGenerationService? service = null)
{
    public static string Key(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public async Task<GenerationOptionsResult> OptionsAsync(Uri uri, string? expectedKey, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var snapshot = await documents.ReadAsync(uri, token);
        var key = Key(snapshot.Text);
        try
        {
            EnsureBounded(snapshot.Text);
            if (expectedKey is not null && expectedKey != key)
                return new(1, uri.AbsoluteUri, snapshot.Version, key, SchemaGenerationService.Catalog, [], [Diagnostic("GEN_STALE", "The selected descriptor changed. Refresh and select it again.")]);
            var document = ContractDocument.Parse(snapshot.Text, baseUri: new Uri(uri, "."));
            var discovery = SchemaGenerationService.Discover(document, token);
            var prefix = document.Format == "odcs" ? "/schema/" : "/resources/";
            return new(1, uri.AbsoluteUri, snapshot.Version, key, discovery.Catalog,
                discovery.Assets.Select(a => new GenerationAssetOption(a.Id, a.Name, prefix + a.Index, a.Eligible, a.Reason)).ToArray(), discovery.Diagnostics);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return new(1, uri.AbsoluteUri, snapshot.Version, key, SchemaGenerationService.Catalog, [],
                [Diagnostic("GEN_DOCUMENT", "The descriptor has no usable canonical schema. Check syntax and mapping diagnostics.")]);
        }
    }

    public async Task<SchemaGenerationResponse> GenerateAsync(SchemaGenerationRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var uri = WorkspaceUri.Parse(request.Uri);
        var snapshot = await documents.ReadAsync(uri, token);
        var key = Key(snapshot.Text);
        if (request.SelectionKey != key || request.Version != snapshot.Version)
            return Failure(request, snapshot, key, "GEN_STALE", "The descriptor changed after selection. Refresh and select it again.");
        try
        {
            return await GenerateCurrentAsync(request, snapshot, key, token);
        }
        catch (OperationCanceledException) { throw; }
        catch (IOException) { return Failure(request, snapshot, key, "GEN_ACCESS", "Unable to read the selected template. Check its location and access permissions."); }
        catch (UnauthorizedAccessException) { return Failure(request, snapshot, key, "GEN_ACCESS", "Unable to access the selected template."); }
        catch (Exception) { return Failure(request, snapshot, key, "GEN_DOCUMENT", "The descriptor or template snapshot is invalid or unsupported."); }
    }

    private async Task<SchemaGenerationResponse> GenerateCurrentAsync(SchemaGenerationRequest request, DocumentSnapshot snapshot, string key, CancellationToken token)
    {
        EnsureBounded(snapshot.Text);
        var document = ContractDocument.Parse(snapshot.Text, baseUri: new Uri(snapshot.Uri, "."));
        if (!MatchesSourcePath(request, document, token))
            return Failure(request, snapshot, key, "GEN_SELECTION", "The selected native/canonical asset no longer matches its source. Refresh and select it again.");
        var template = await ReadTemplateAsync(request.Template, token);
        var result = await (service ?? new SchemaGenerationService()).GenerateAsync(document, request.Options, template, token);
        token.ThrowIfCancellationRequested();
        if (!await IsCurrentAsync(snapshot, token) || !await TemplateCurrentAsync(request.Template, template, token))
            return Failure(request, snapshot, key, "GEN_STALE", "The descriptor or template changed during generation. Generate again from the current buffers.");
        var locations = new GenerationLocations(document, snapshot.Uri);
        return new(1, snapshot.Uri.AbsoluteUri, snapshot.Version, key, result with { Context = null },
            result.Diagnostics.Select(d => new GenerationFinding(d, locations.Find(d))).ToArray());
    }

    private static SchemaGenerationResponse Failure(SchemaGenerationRequest request, DocumentSnapshot snapshot, string key, string code, string message)
        => new(1, snapshot.Uri.AbsoluteUri, snapshot.Version, key,
            new(null, null, null, request.Options.AssetId, request.Template?.Uri, request.Template?.Version, [Diagnostic(code, message)]), []);

    private static bool MatchesSourcePath(SchemaGenerationRequest request, ContractDocument document, CancellationToken token)
    {
        if (request.SourcePath is null) return true;
        var assets = SchemaGenerationService.Discover(document, token).Assets;
        var prefix = document.Format == "odcs" ? "/schema/" : "/resources/";
        return assets.Any(a => a.Eligible && a.Id == request.Options.AssetId && prefix + a.Index == request.SourcePath);
    }

    private async Task<TemplateInput?> ReadTemplateAsync(GenerationTemplateSnapshot? template, CancellationToken token)
    {
        if (template is null) return null;
        var uri = WorkspaceUri.Parse(template.Uri);
        if (!uri.IsFile) throw new IOException("Only explicitly selected local templates are supported.");
        var text = template.Text ?? (await documents.ReadAsync(uri, token)).Text;
        if (Encoding.UTF8.GetByteCount(text) > ContractService.MaximumDocumentBytes) throw new IOException("Template is too large.");
        return new(uri.AbsoluteUri, text, template.Version);
    }

    private async Task<bool> IsCurrentAsync(DocumentSnapshot snapshot, CancellationToken token)
        => await documents.ReadAsync(snapshot.Uri, token) == snapshot;

    private async Task<bool> TemplateCurrentAsync(GenerationTemplateSnapshot? request, TemplateInput? captured, CancellationToken token)
    {
        if (request is null || captured is null) return true;
        var uri = WorkspaceUri.Parse(request.Uri);
        var open = documents.OpenDocuments.SingleOrDefault(d => d.Uri == uri);
        if (open is not null) return open.Text == captured.Text && open.Version == request.Version;
        // Explicit unsynchronized editor snapshots are validated again by the client.
        if (request.Text is not null) return true;
        return (await documents.ReadAsync(uri, token)).Text == captured.Text;
    }

    private static GenerationDiagnostic Diagnostic(string code, string message) => new(code, "Error", "$", message);

    private static void EnsureBounded(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > ContractService.MaximumDocumentBytes) throw new FormatException("Descriptor snapshot is too large.");
    }
}
