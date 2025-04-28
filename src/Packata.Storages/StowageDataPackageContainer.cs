using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Storage;
using Stowage;

namespace Packata.Storages;

internal class StowageDataPackageContainer : IDataPackageContainer, IDataPackageContainerListable
{
    public Uri BaseUri { get; }
    internal IFileStorage Storage { get; }

    public StowageDataPackageContainer(Uri baseUri, IFileStorage storage)
    {
        BaseUri = baseUri;
        Storage = storage;
    }

    public async Task<Stream> OpenAsync(string relativePath)
    {
        ThrowIfDisposed();
        return await Storage.OpenRead(relativePath)
            ?? throw new FileNotFoundException($"File '{relativePath}' not found.");
    }

    public async Task<IEnumerable<string>> ListAsync()
    {
        ThrowIfDisposed();
        var entries = await Storage.Ls("");
        return entries.Select(e => e.Path.Full);
    }

    public async Task<bool> ExistsAsync(string relativePath)
    {
        ThrowIfDisposed();
        return await Storage.Exists(relativePath);
    }

    private bool _disposed = false;

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, nameof(StowageDataPackageContainer));

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Storage.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
