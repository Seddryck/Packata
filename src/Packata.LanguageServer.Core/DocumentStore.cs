using System.Collections.Concurrent;

namespace Packata.LanguageServer.Core;

public sealed class DocumentStore
{
    private readonly ConcurrentDictionary<Uri, DocumentSnapshot> open = new();
    public IEnumerable<DocumentSnapshot> OpenDocuments => open.Values;

    public bool Update(Uri uri, string text, int version)
    {
        uri = WorkspaceUri.Parse(uri.AbsoluteUri);
        while (true)
        {
            if (!open.TryGetValue(uri, out var previous))
            {
                if (open.TryAdd(uri, new(uri, text, version))) return true;
            }
            else
            {
                if (previous.Version >= version) return false;
                if (open.TryUpdate(uri, new(uri, text, version), previous)) return true;
            }
        }
    }

    public void Close(Uri uri) => open.TryRemove(WorkspaceUri.Parse(uri.AbsoluteUri), out _);
    public bool IsCurrent(DocumentSnapshot snapshot) => open.TryGetValue(WorkspaceUri.Parse(snapshot.Uri.AbsoluteUri), out var current)
        ? current.Version == snapshot.Version : snapshot.Version is null;
    public async Task<DocumentSnapshot> ReadAsync(Uri uri, CancellationToken token)
    {
        uri = WorkspaceUri.Parse(uri.AbsoluteUri);
        if (open.TryGetValue(uri, out var document)) return document;
        if (!uri.IsFile) throw new NotSupportedException("Only local workspace files and synchronized buffers are supported.");
        if (new FileInfo(uri.LocalPath).Length > ContractService.MaximumDocumentBytes)
            throw new IOException("Descriptor exceeds the document size limit.");
        var text = await File.ReadAllTextAsync(uri.LocalPath, token);
        // didOpen can arrive while disk I/O is in progress.
        return open.TryGetValue(uri, out document) ? document : new(uri, text, null);
    }
}
