using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Storage;

namespace Packata.Storages;
internal class ZipContainerWrapper : IContainerWrapper
{
    public async Task<IDocumentContainer> WrapAsync(IDocumentContainer baseContainer, Uri uri)
    {
        var stream = await baseContainer.OpenAsync("");
        return new ZipDataPackageContainer(uri, stream);
    }
}
