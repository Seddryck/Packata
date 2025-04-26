using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
internal class ContainerPath : IPath
{
    public string RelativePath { get; }
    private readonly IDataPackageContainer _container;

    public ContainerPath(string relativePath, IDataPackageContainer container)
    {
        RelativePath = relativePath;
        _container = container;
    }

    public Task<Stream> OpenAsync()
        => _container.OpenAsync(RelativePath);
    public Task<bool> ExistsAsync()
        => _container.ExistsAsync(RelativePath);

    public override string ToString() => RelativePath;
}
