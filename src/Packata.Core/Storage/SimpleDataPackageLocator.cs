using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
internal class SimpleDataPackageLocator : IDataPackageLocator
{
    public bool CanHandle(Uri containerUri)
        => containerUri.Scheme.StartsWith(Uri.UriSchemeFile);

    public Task<DataPackageHandle> LocateAsync(Uri containerUri, string descriptorPath = "datapackage.json")
    {
        var container = new LocalDirectoryDataPackageContainer(containerUri);
        return Task.FromResult(new DataPackageHandle(container, descriptorPath));
    }
}
