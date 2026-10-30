using System.Collections.Concurrent;
using Packata.Core.Contracts;
using Packata.Application;
using YamlDotNet.Core;

namespace Packata.LanguageServer.Core;

public sealed class ContractService(DocumentStore documents)
{
    public const int SchemaVersion = 1;
    public const int MaximumDocumentBytes = 4 * 1024 * 1024;
    private sealed record Analysis(DocumentSnapshot Snapshot, Inspection Inspection, ContractDocument? Document);
    private readonly ConcurrentDictionary<Uri, Analysis> cache = new();

    public async Task<Inspection> InspectAsync(Uri uri, CancellationToken token)
    {
        var snapshot = await documents.ReadAsync(uri, token);
        token.ThrowIfCancellationRequested();
        return Inspect(snapshot, token);
    }

    public Inspection Inspect(DocumentSnapshot snapshot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (cache.TryGetValue(snapshot.Uri, out var previous) && previous.Snapshot == snapshot)
            return previous.Inspection;
        var baseUri = new Uri(snapshot.Uri, ".").AbsoluteUri;
        var recognized = DescriptorName(snapshot.Uri);
        ContractSyntax? syntax = null;
        try
        {
            if (System.Text.Encoding.UTF8.GetByteCount(snapshot.Text) > MaximumDocumentBytes) throw new InvalidDataException();
            syntax = ContractSyntax.Parse(snapshot.Text);
            recognized |= syntax.Recognized;
            if (!recognized) return new(1, snapshot.Uri.AbsoluteUri, snapshot.Version, baseUri, false, null, null, [], null);
            var analysis = Analyze(snapshot, syntax, baseUri, token);
            cache[snapshot.Uri] = analysis;
            return analysis.Inspection;
        }
        catch (Exception error) when (IsDocumentError(error))
        {
            recognized |= previous?.Inspection.Recognized == true;
            var inspection = InvalidInspection(snapshot, baseUri, recognized, error, syntax);
            cache[snapshot.Uri] = new(snapshot, inspection, null);
            return inspection;
        }
    }

    private static Analysis Analyze(DocumentSnapshot snapshot, ContractSyntax syntax, string baseUri, CancellationToken token)
    {
        var analysis = ContractInspector.Inspect(syntax, baseUri: new Uri(baseUri), cancellationToken: token);
        var document = analysis.Document;
        var mapped = analysis.Mapping;
        var index = new SourceIndex(document.Root);
        var diagnostics = mapped.Diagnostics.Select(value => Diagnostic(value, index)).ToArray();
        var inspection = new Inspection(1, snapshot.Uri.AbsoluteUri, snapshot.Version, baseUri, true, document.Format,
            mapped.Value is null ? null : ContractJson.RedactedJson(mapped.Value), diagnostics,
            mapped.Value is null ? null : new ContractTreeBuilder(document, snapshot.Uri).Build(mapped.Value),
            new ContractTreeBuilder(document, snapshot.Uri).BuildNative(),
            mapped.Value is null ? [] : ResourcePreview.Targets(document, mapped.Value, new Uri(baseUri)));
        token.ThrowIfCancellationRequested();
        return new(snapshot, inspection, document);
    }

    private static bool IsDocumentError(Exception error) => error is YamlException or FormatException
        or InvalidDataException or Newtonsoft.Json.JsonException or ArgumentException or InvalidOperationException;

    private static Inspection InvalidInspection(DocumentSnapshot snapshot, string baseUri, bool recognized, Exception error, ContractSyntax? syntax)
    {
        // Parser messages may contain source excerpts or credentials. Only expose a fixed message.
        var diagnostics = recognized
            ? new[] { new DocumentDiagnostic("PKT001", "Error", "$", "Invalid or unsupported contract document. Check syntax, format, and document size (4 MiB maximum).", FailureRange(error)) }
            : [];
        var native = TryNativeTree(snapshot, recognized, syntax);
        return new(1, snapshot.Uri.AbsoluteUri, snapshot.Version, baseUri, recognized, native.Format, null, diagnostics, null, native.Tree);
    }

    private static (string? Format, ContractNode? Tree) TryNativeTree(DocumentSnapshot snapshot, bool recognized, ContractSyntax? syntax)
    {
        if (!recognized || System.Text.Encoding.UTF8.GetByteCount(snapshot.Text) > MaximumDocumentBytes) return (null, null);
        try
        {
            var root = (syntax ?? ContractSyntax.Parse(snapshot.Text)).Root;
            var odcs = ContractDocument.Child(root, "kind") is not null;
            return (odcs ? "odcs" : "datapackage", new ContractTreeBuilder(root, odcs, snapshot.Uri).BuildNative());
        }
        catch (Exception error) when (IsDocumentError(error))
        {
            // Broken or unsafe syntax has diagnostics but cannot provide a reliable source tree.
            return (null, null);
        }
    }

    private static SourceRange? FailureRange(Exception error)
    {
        if (error is not YamlException yaml || yaml.Start.Line <= 0) return null;
        var position = new Position((int)yaml.Start.Line - 1, (int)Math.Max(0, yaml.Start.Column - 1));
        return new(position, position);
    }

    public async Task<ValidationResult> ValidateAsync(Uri uri, CancellationToken token, IReadOnlyList<string>? checks = null)
    {
        var snapshot = await documents.ReadAsync(uri, token);
        token.ThrowIfCancellationRequested();
        var result = SelectedValidation.Validate(snapshot, checks, token);
        token.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<ValidationOptions> ValidationOptionsAsync(Uri uri, CancellationToken token)
        => SelectedValidation.Options(await documents.ReadAsync(uri, token));

    public async Task<PreviewResult> PreviewAsync(Uri uri, string sourcePath, CancellationToken token,
        int limit = ResourcePreview.DefaultLimit, int? bindingIndex = null, string? selectionKey = null)
    {
        if (limit <= 0) return new(1, uri.AbsoluteUri, null, limit, [], [],
            new("PREVIEW_LIMIT", "Error", "$", "The row limit must be a positive integer.", null));
        return await ResourcePreview.ReadAsync(await documents.ReadAsync(uri, token), sourcePath, limit, bindingIndex, token, selectionKey: selectionKey);
    }

    public async Task<ValidationResult> SyntaxAsync(Uri uri, CancellationToken token)
    {
        var snapshot = await documents.ReadAsync(uri, token);
        var diagnostics = SelectedValidation.SyntaxDiagnostics(snapshot.Text);
        return new(1, uri.AbsoluteUri, snapshot.Version, diagnostics.Length == 0, diagnostics);
    }

    public async Task<DiscoveryResult> DiscoverAsync(IEnumerable<string> folders, CancellationToken token)
    {
        var candidates = Candidates(folders, token);
        foreach (var removed in cache.Keys.Where(uri => !candidates.Contains(uri))) cache.TryRemove(removed, out _);
        var results = new List<Inspection>();
        foreach (var uri in candidates.OrderBy(value => value.AbsoluteUri, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var inspection = await InspectIfReadableAsync(uri, token);
            if (inspection?.Recognized == true) results.Add(inspection);
        }
        return new(1, results);
    }

    private HashSet<Uri> Candidates(IEnumerable<string> folders, CancellationToken token)
    {
        var candidates = new HashSet<Uri>();
        foreach (var uri in folders.Select(WorkspaceUri.Parse).Where(uri => uri.IsFile && Directory.Exists(uri.LocalPath)))
            foreach (var file in Files(uri.LocalPath, token)) candidates.Add(new Uri(file));
        candidates.UnionWith(documents.OpenDocuments.Where(document => Candidate(document.Uri.LocalPath)).Select(document => document.Uri));
        return candidates;
    }

    private async Task<Inspection?> InspectIfReadableAsync(Uri uri, CancellationToken token)
    {
        try { return await InspectAsync(uri, token); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A watcher will retry files that disappear or become inaccessible during a scan.
            return null;
        }
    }

    private static IEnumerable<string> Files(string directory, CancellationToken token)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", options))
        {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(entry))
            {
                if (Path.GetFileName(entry) is ".git" or ".worktrees" or "node_modules" or "bin" or "obj" or ".vs") continue;
                foreach (var child in Files(entry, token)) yield return child;
            }
            else if (Candidate(entry) && new FileInfo(entry).Length <= MaximumDocumentBytes) yield return entry;
        }
    }

    private static bool Candidate(string path) => Path.GetExtension(path).ToLowerInvariant() is ".yaml" or ".yml" or ".json";
    private static bool DescriptorName(Uri uri) => Path.GetFileName(uri.LocalPath).ToLowerInvariant() is
        "datapackage.json" or "datapackage.yaml" or "datapackage.yml" or "contract.yaml" or "contract.yml";
    private static DocumentDiagnostic Diagnostic(MappingDiagnostic value, SourceIndex index) => new(value.Code,
        value.Severity.ToString(), ContractJson.Redact(value.SourcePath), ContractJson.Redact(value.Message), index.DiagnosticRange(value.SourcePath));
}
