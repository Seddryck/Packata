using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Storage;

namespace Packata.Storages;
internal class ZipContainerWrapper(CancellationToken cancellationToken = default) : IContainerWrapper
{
    public async Task<IDocumentContainer> WrapAsync(IDocumentContainer baseContainer, Uri uri)
    {
        var stream = await baseContainer.OpenAsync(ArchiveSource.Path(uri)).ConfigureAwait(false);
        try
        {
            if (!stream.CanSeek)
            {
                var staged = await SeekableStaging.CopyAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                await stream.DisposeAsync().ConfigureAwait(false);
                stream = staged;
            }
            return new ZipDataPackageContainer(uri, stream);
        }
        catch { await stream.DisposeAsync().ConfigureAwait(false); throw; }
    }
}
