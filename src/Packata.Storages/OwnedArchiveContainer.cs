using Packata.Core.Storage;

namespace Packata.Storages;

internal sealed class OwnedArchiveContainer(IDocumentContainer archive, IDocumentContainer source)
    : IDocumentContainerListable
{
    public Uri BaseUri => archive.BaseUri;
    public Task<Stream> OpenAsync(string relativePath) => archive.OpenAsync(relativePath);
    public Task<bool> ExistsAsync(string relativePath) => archive.ExistsAsync(relativePath);
    public Task<IEnumerable<string>> ListAsync() => archive is IDocumentContainerListable listable
        ? listable.ListAsync()
        : throw new NotSupportedException("This archive does not support listing entries.");

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { archive.Dispose(); }
        finally { source.Dispose(); }
    }
}
