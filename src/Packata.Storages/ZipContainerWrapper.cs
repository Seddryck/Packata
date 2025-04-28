using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Storage;

namespace Packata.Storages;
internal class ZipContainerWrapper : IContainerWrapper
{
    public async Task<IDataPackageContainer> WrapAsync(IDataPackageContainer baseContainer, Uri uri)
    {
        var stream = await baseContainer.OpenAsync("");
<<<<<<< HEAD
        return new ZipDataPackageContainer(uri, stream);
=======
        return new ZipDataPackageContainer(new Uri("zip"), stream);
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
    }
}
