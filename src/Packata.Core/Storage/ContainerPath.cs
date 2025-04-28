using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
public class ContainerPath : IPath
{
    public string RelativePath { get; }
    public string Value => RelativePath;
    private readonly IDataPackageContainer _container;

    public bool IsFullyQualified => false;

    public ContainerPath(string relativePath, IDataPackageContainer container)
    {
        RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
        _container = container ?? throw new ArgumentNullException(nameof(container));
    }

    public Task<Stream> OpenAsync()
        => _container.OpenAsync(RelativePath);
    public Task<bool> ExistsAsync()
        => _container.ExistsAsync(RelativePath);

    public override string ToString() => RelativePath;
}
