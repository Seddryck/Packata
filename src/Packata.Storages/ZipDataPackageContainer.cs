using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Storage;

namespace Packata.Storages;
public class ZipDataPackageContainer : IDataPackageContainer
{
    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    private readonly ZipArchive _archive;

    public Uri BaseUri { get; }

    public ZipDataPackageContainer(Uri uri, Stream zipStream)
    {
        BaseUri = uri;
<<<<<<< HEAD
        _archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
=======
        _archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb

        _entries = _archive.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name)) // exclude directories
            .ToDictionary(
                e => NormalizePath(e.FullName),
                e => e,
                StringComparer.OrdinalIgnoreCase
            );
    }

    public Task<Stream> OpenAsync(string relativePath)
    {
        ThrowIfDisposed();
        var key = NormalizePath(relativePath);

        if (!_entries.TryGetValue(key, out var entry))
            throw new FileNotFoundException($"'{relativePath}' not found in zip archive.");

        // Note: Open returns a new stream every time
        return Task.FromResult(entry.Open() as Stream);
    }

    public Task<IEnumerable<string>> ListFilesAsync()
        => Task.FromResult(_entries.Keys.AsEnumerable().Select(x => NormalizePath(x)));

    private static string NormalizePath(string path)
        => path.Replace('\\', '/').TrimStart('/');

    public Task<bool> ExistsAsync(string relativePath)
        => Task.FromResult(_entries.ContainsKey(NormalizePath(relativePath)));

    private bool _disposed = false;
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _archive?.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, _archive);
}
