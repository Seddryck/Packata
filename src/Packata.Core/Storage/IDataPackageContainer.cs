using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
public interface IDataPackageContainer : IDisposable
{
    Uri BaseUri { get; }
    Task<Stream> OpenAsync(string relativePath);
    Task<bool> ExistsAsync(string relativePath);
}

public interface IDataPackageContainerListable : IDataPackageContainer
{
    Task<IEnumerable<string>> ListAsync();
}
