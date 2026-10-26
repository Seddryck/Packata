using Packata.Core.Reading;
using Packata.ResourceReaders;
using Packata.Storages;

namespace Packata.Cli;

// The resolver owns acquisition; the returned stream transfers ownership to the reader/inferrer.
// Resource identities remain on the request, independent of any temporary file.
internal sealed class ResourceAcquisition(IEndpointStreamResolver resolver, ResolvedDataFormat format,
    long maximumBytes, long? workbookMaximumBytes = null) : IEndpointStreamResolver
{
    public async ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var seekable = format.Name is "xls" or "xlsx" or "parquet" or "pqt";
        if (seekable && format.Compression is not null)
            throw new NotSupportedException("Externally compressed binary files are unsupported.");
        var stream = await resolver.OpenAsync(path, cancellationToken).ConfigureAwait(false);
        if (!seekable)
            return stream.CanSeek ? stream : new BoundedReadStream(stream, SeekableStaging.DefaultMaximumBytes, cancellationToken);
        var limit = Math.Min(maximumBytes, workbookMaximumBytes ?? long.MaxValue);
        try { return await SeekableStaging.OpenOwnedAsync(stream, limit, cancellationToken).ConfigureAwait(false); }
        catch (StagingLimitExceededException error) when (workbookMaximumBytes < maximumBytes)
        {
            throw new InvalidDataException("Workbook exceeds MaximumBytes during transfer/staging.", error);
        }
    }
}
