using System.IO.Compression;
using Packata.Core.Storage;

namespace Packata.Storages;

internal sealed class TarContainerWrapper(ArchiveCompression compression, TarArchiveOptions options, CancellationToken cancellationToken = default) : IContainerWrapper
{
    public async Task<IDocumentContainer> WrapAsync(IDocumentContainer baseContainer, Uri uri)
    {
        await using var source = await baseContainer.OpenAsync(ArchiveSource.Path(uri)).ConfigureAwait(false);
        using Stream decoded = compression switch
        {
            ArchiveCompression.None => source,
            ArchiveCompression.Gzip => new GZipStream(source, CompressionMode.Decompress, leaveOpen: true),
            ArchiveCompression.Brotli => new BrotliStream(source, CompressionMode.Decompress, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(compression))
        };
        try
        {
            return await TarDataPackageContainer.CreateAsync(uri, decoded, options, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (compression == ArchiveCompression.Brotli)
        {
            throw new InvalidDataException("The Brotli-compressed TAR archive is invalid.", ex);
        }
    }
}
