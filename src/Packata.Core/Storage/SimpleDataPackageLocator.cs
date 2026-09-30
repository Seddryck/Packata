using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
internal class SimpleDocumentLocator : IDocumentLocator
{
    public bool CanHandle(Uri containerUri)
        => containerUri.Scheme.StartsWith(Uri.UriSchemeFile);

    public Task<DocumentHandle> LocateAsync(Uri containerUri, string descriptorPath = "datapackage.json")
    {
        var container = new LocalDirectoryDocumentContainer(containerUri);
        return Task.FromResult(new DocumentHandle(container, descriptorPath));
    }
}
