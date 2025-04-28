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
    private readonly IStorageProvider _provider;

    public PathFactory(IDataPackageContainer container, IStorageProvider provider)
        => (_container, _provider) = (container, provider);

    public IPath Create(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        var pathUri = new Uri(path, UriKind.RelativeOrAbsolute);
        return pathUri.IsAbsoluteUri
            ? new FullyQualifiedPath(path, _provider)
            : new ContainerPath(path, _container);
    }
}
