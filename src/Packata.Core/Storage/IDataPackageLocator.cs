using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
public interface IDocumentLocator
{
    Task<DocumentHandle> LocateAsync(Uri containerUri, string descriptorName = "datapackage.json");

    bool CanHandle(Uri containerUri);
}
