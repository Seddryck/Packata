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
        var uri = snapshot.Uri.AbsoluteUri;
        var baseUri = snapshot.Uri.IsAbsoluteUri ? new Uri(snapshot.Uri, ".").AbsoluteUri : uri;
        var recognized = DescriptorName(snapshot.Uri);
        try
        {
            if (System.Text.Encoding.UTF8.GetByteCount(snapshot.Text) > MaximumDocumentBytes) throw new InvalidDataException();
            var yaml = new YamlStream();
            yaml.Load(new StringReader(snapshot.Text));
            recognized = recognized || (yaml.Documents.Count == 1 && yaml.Documents[0].RootNode is YamlMappingNode root && ContractDocument.Recognizes(root));
            if (!recognized) return new(1, uri, snapshot.Version, baseUri, false, null, null, [], null);
            var document = ContractDocument.Parse(snapshot.Text, baseUri: new Uri(baseUri));
            var mapped = document.Map();
            token.ThrowIfCancellationRequested();
            var index = new SourceIndex(document.Root);
            var diagnostics = mapped.Diagnostics.Select(value => Diagnostic(value, index)).ToArray();
            var inspection = new Inspection(1, uri, snapshot.Version, baseUri, true, document.Format,
                mapped.Value is null ? null : ContractJson.RedactedJson(mapped.Value), diagnostics,
                mapped.Value is null ? null : new ContractTreeBuilder(document, snapshot.Uri).Build(mapped.Value));
            cache[snapshot.Uri] = new(snapshot, inspection, document);
            return inspection;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is YamlException or FormatException or InvalidDataException or Newtonsoft.Json.JsonException or ArgumentException or InvalidOperationException)
        {
            recognized |= previous?.Inspection.Recognized == true;
            // Parser messages may contain source excerpts and credentials. Only expose a fixed message.
            SourceRange? range = error is YamlException yaml && yaml.Start.Line > 0
                ? new(new((int)yaml.Start.Line - 1, (int)Math.Max(0, yaml.Start.Column - 1)), new((int)yaml.Start.Line - 1, (int)Math.Max(0, yaml.Start.Column - 1))) : null;
            var inspection = new Inspection(1, uri, snapshot.Version, baseUri, recognized, null, null,
                recognized ? [new("PKT001", "Error", "$", "Invalid or unsupported contract document. Check syntax, format, and document size (4 MiB maximum).", range)] : [], null);
            cache[snapshot.Uri] = new(snapshot, inspection, null);
            return inspection;
        }
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
        var candidates = new HashSet<Uri>();
        foreach (var folder in folders)
        {
            var uri = WorkspaceUri.Parse(folder);
            if (!uri.IsFile || !Directory.Exists(uri.LocalPath)) continue;
            foreach (var file in Files(uri.LocalPath, token)) candidates.Add(new Uri(file));
        }
        foreach (var document in documents.OpenDocuments)
            if (Candidate(document.Uri.LocalPath)) candidates.Add(document.Uri);
        foreach (var removed in cache.Keys.Where(uri => !candidates.Contains(uri))) cache.TryRemove(removed, out _);
        var results = new List<Inspection>();
        foreach (var uri in candidates.OrderBy(value => value.AbsoluteUri, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var inspection = await InspectAsync(uri, token);
                if (inspection.Recognized) results.Add(inspection);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* A watcher will retry transient workspace changes. */ }
        }
        return new(1, results);
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
