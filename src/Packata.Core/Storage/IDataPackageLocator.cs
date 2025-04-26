using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
public interface IDataPackageLocator
{
    Task<DataPackageHandle> LocateAsync(Uri containerUri, string descriptorName = "datapackage.json");

    bool CanHandle(Uri containerUri);
}
