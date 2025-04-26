using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Storage;

namespace Packata.Core.Storage;
public class PathFactory
{
    private readonly IDataPackageContainer _container;

    public PathFactory(IDataPackageContainer container)
        => _container = container;

    public IPath Create(string path)
        => new ContainerPath(path, _container);
}
