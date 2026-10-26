using System.Formats.Tar;
using Packata.Core.Storage;

namespace Packata.Storages;

/// <summary>A materialized TAR archive with independent resource streams.</summary>
public sealed class TarDataPackageContainer : IDocumentContainerListable
{
    private readonly Dictionary<string, StoredEntry> _entries = new(StringComparer.Ordinal);
    private bool _disposed;
    public Uri BaseUri { get; }

    private TarDataPackageContainer(Uri uri) => BaseUri = uri;

    /// <summary>Consumes and closes <paramref name="tarStream"/>, including on failure.</summary>
    public static async Task<TarDataPackageContainer> CreateAsync(Uri uri, Stream tarStream,
        TarArchiveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tarStream);
        using (tarStream)
        {
            ArgumentNullException.ThrowIfNull(uri);
            options ??= new TarArchiveOptions();
            options.Validate();
            var container = new TarDataPackageContainer(uri);
            try
            {
                await container.InitializeAsync(tarStream, options).ConfigureAwait(false);
                return container;
            }
            catch
            {
                container.Dispose();
                throw;
            }
        }
    }

    private async Task InitializeAsync(Stream source, TarArchiveOptions options)
    {
        using var bounded = new BoundedArchiveStream(source, options.MaxArchiveBytes);
        using var reader = new TarReader(bounded, leaveOpen: true);
        long total = 0, memory = 0;
        var count = 0;
        while (await reader.GetNextEntryAsync(copyData: false).ConfigureAwait(false) is { } entry)
        {
            if (++count > options.MaxEntries) throw new InvalidDataException("TAR entry count limit exceeded.");
            var name = NormalizePath(entry.Name);
            if (entry.EntryType == TarEntryType.Directory) continue;
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                throw new NotSupportedException($"TAR entry '{name}' has unsupported type '{entry.EntryType}'.");
            if (name.Length == 0) throw new InvalidDataException("A TAR file entry must have a resource path.");
            if (_entries.ContainsKey(name)) throw new InvalidDataException($"Duplicate TAR entry '{name}'.");
            if (entry.Length > options.MaxEntryBytes || entry.Length > options.MaxTotalBytes - total)
                throw new InvalidDataException($"TAR size limit exceeded by '{name}'.");
            total += entry.Length;

            StoredEntry stored;
            if (entry.Length <= options.MemoryThresholdBytes - memory && entry.Length <= int.MaxValue)
            {
                var bytes = new byte[(int)entry.Length];
                if (entry.DataStream is not null)
                    await entry.DataStream.ReadExactlyAsync(bytes).ConfigureAwait(false);
                memory += bytes.Length;
                stored = new StoredEntry(bytes, null);
            }
            else
            {
                var path = Path.Combine(options.TemporaryDirectory, $"packata-tar-{Guid.NewGuid():N}.tmp");
                var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                    FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                try
                {
                    if (entry.DataStream is not null)
                        await entry.DataStream.CopyToAsync(file).ConfigureAwait(false);
                    if (file.Length != entry.Length) throw new InvalidDataException($"Truncated TAR entry '{name}'.");
                    await file.FlushAsync().ConfigureAwait(false);
                    stored = new StoredEntry(null, file);
                }
                catch { file.Dispose(); throw; }
            }
            _entries.Add(name, stored);
        }
        // Finish decoding, including the compression footer, and enforce the full stream limit.
        await bounded.CopyToAsync(Stream.Null).ConfigureAwait(false);
        if (bounded.BytesRead < 1024) throw new InvalidDataException("The TAR archive is truncated.");
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = path.Replace('\\', '/');
        if (path.StartsWith('/') || path.Contains(':') || path.Contains('\0'))
            throw new InvalidDataException($"Invalid TAR resource path '{path}'.");
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Contains("..")) throw new InvalidDataException($"Invalid TAR resource path '{path}'.");
        var normalized = string.Join('/', parts.Where(part => part != "."));
        // A TAR directory entry named '.' is permitted, but cannot be opened as a resource.
        return normalized;
    }

    public Task<Stream> OpenAsync(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = NormalizePath(relativePath);
        if (!_entries.TryGetValue(key, out var entry))
            throw new FileNotFoundException($"'{relativePath}' not found in TAR archive.");
        return Task.FromResult(entry.Open());
    }

    public Task<bool> ExistsAsync(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.FromResult(_entries.ContainsKey(NormalizePath(relativePath)));
    }

    public Task<IEnumerable<string>> ListAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.FromResult<IEnumerable<string>>(_entries.Keys.ToArray());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _entries.Values) entry.Dispose();
        _entries.Clear();
    }

    private sealed record StoredEntry(byte[]? Bytes, FileStream? File) : IDisposable
    {
        public Stream Open() => Bytes is not null
            ? new MemoryStream(Bytes, writable: false)
            : new FileStream(File!.Name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        public void Dispose() => File?.Dispose();
    }
}
