using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Storage;
using Stowage;

namespace Packata.Storages;
<<<<<<< HEAD
internal class StowageDataPackageContainer : IDataPackageContainer, IDataPackageContainerListable
=======
internal class StowageDataPackageContainer : IDataPackageContainer
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
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

<<<<<<< HEAD
    public async Task<IEnumerable<string>> ListAsync()
=======
    public async Task<IEnumerable<string>> ListFilesAsync()
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
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
<<<<<<< HEAD
=======

>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
