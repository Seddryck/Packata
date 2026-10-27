using System.Collections.Concurrent;
using Packata.Core.Contracts;
using Packata.Documents;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

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
        try
        {
            recognized |= RecognizesContent(snapshot.Text);
            if (!recognized) return new(1, snapshot.Uri.AbsoluteUri, snapshot.Version, baseUri, false, null, null, [], null);
            var analysis = Analyze(snapshot, baseUri, token);
            cache[snapshot.Uri] = analysis;
            return analysis.Inspection;
        }
        catch (Exception error) when (IsDocumentError(error))
        {
            recognized |= previous?.Inspection.Recognized == true;
            var inspection = InvalidInspection(snapshot, baseUri, recognized, error);
            cache[snapshot.Uri] = new(snapshot, inspection, null);
            return inspection;
        }
    }

    private static bool RecognizesContent(string text)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) > MaximumDocumentBytes) throw new InvalidDataException();
        var yaml = new YamlStream();
        yaml.Load(new StringReader(text));
        return yaml.Documents.Count == 1 && yaml.Documents[0].RootNode is YamlMappingNode root && ContractDocument.Recognizes(root);
    }

    private static Analysis Analyze(DocumentSnapshot snapshot, string baseUri, CancellationToken token)
    {
        var document = ContractDocument.Parse(snapshot.Text, baseUri: new Uri(baseUri));
        var mapped = document.Map();
        token.ThrowIfCancellationRequested();
        var index = new SourceIndex(document.Root);
        var diagnostics = mapped.Diagnostics.Select(value => Diagnostic(value, index)).ToArray();
        var inspection = new Inspection(1, snapshot.Uri.AbsoluteUri, snapshot.Version, baseUri, true, document.Format,
            mapped.Value is null ? null : ContractJson.RedactedJson(mapped.Value), diagnostics,
            mapped.Value is null ? null : new ContractTreeBuilder(document, snapshot.Uri).Build(mapped.Value));
        return new(snapshot, inspection, document);
    }

    private static bool IsDocumentError(Exception error) => error is YamlException or FormatException
        or InvalidDataException or Newtonsoft.Json.JsonException or ArgumentException or InvalidOperationException;

    private static Inspection InvalidInspection(DocumentSnapshot snapshot, string baseUri, bool recognized, Exception error)
    {
        // Parser messages may contain source excerpts or credentials. Only expose a fixed message.
        var diagnostics = recognized
            ? new[] { new DocumentDiagnostic("PKT001", "Error", "$", "Invalid or unsupported contract document. Check syntax, format, and document size (4 MiB maximum).", FailureRange(error)) }
            : [];
        return new(1, snapshot.Uri.AbsoluteUri, snapshot.Version, baseUri, recognized, null, null, diagnostics, null);
    }

    private static SourceRange? FailureRange(Exception error)
    {
        if (error is not YamlException yaml || yaml.Start.Line <= 0) return null;
        var position = new Position((int)yaml.Start.Line - 1, (int)Math.Max(0, yaml.Start.Column - 1));
        return new(position, position);
    }

    public async Task<ValidationResult> ValidateAsync(Uri uri, CancellationToken token)
    {
        var snapshot = await documents.ReadAsync(uri, token);
        var inspection = Inspect(snapshot, token);
        if (!inspection.Recognized || inspection.Contract is null)
            return new(1, uri.AbsoluteUri, snapshot.Version, false, inspection.Diagnostics);
        var document = cache.TryGetValue(uri, out var entry) && entry.Snapshot == snapshot && entry.Document is not null
            ? entry.Document : ContractDocument.Parse(snapshot.Text, baseUri: new Uri(inspection.BaseUri));
        var index = new SourceIndex(document.Root);
        var diagnostics = inspection.Diagnostics.Concat(ContractDocument.Validate(document.NativeDocument)
            .Select(value => Diagnostic(value, index))).Distinct().ToArray();
        token.ThrowIfCancellationRequested();
        return new(1, uri.AbsoluteUri, snapshot.Version, diagnostics.All(value => value.Severity != "Error"), diagnostics);
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
