using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;
using Packata.Core.Storage;
using Stowage;
using Stowage.Impl.Microsoft;

namespace Packata.Storages;
public class DataPackageLocator : IDataPackageLocator
{
    private readonly IDictionary<string, Func<Uri, IDataPackageContainer>> _containers;
    private readonly IDictionary<string, Func<Uri, IContainerWrapper>> _wrappers;

    protected internal DataPackageLocator(
        IDictionary<string, Func<Uri, IDataPackageContainer>> containers,
        IDictionary<string, Func<Uri, IContainerWrapper>> wrappers)
    {
        (_containers, _wrappers) = (containers, wrappers);
    }

    public Task<DataPackageHandle> LocateAsync(Uri containerUri, string descriptorName = "datapackage.json")
    {
        var container = GetContainer(containerUri);
        return Task.FromResult(new DataPackageHandle(container, descriptorName));
    }

    protected internal virtual IDataPackageContainer GetContainer(Uri uri)
    {
        if (_containers.TryGetValue(uri.Scheme, out var storage))
            return storage.Invoke(uri);
        throw new NotSupportedException($"No container found for URI: {uri}");
    }

<<<<<<< HEAD
    protected internal virtual IContainerWrapper GetWrapper(Uri uri)
=======
    protected internal virtual IContainerWrapper? GetWrapper(Uri uri)
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
    {
        if (_wrappers.TryGetValue(uri.GetExtension() ?? string.Empty, out var storage))
            return storage.Invoke(uri);
        throw new NotSupportedException($"No wrapper found for URI: {uri}");
    }

    protected virtual bool CanHandle(UriComponents component, Uri uri)
        => component switch
        {
            UriComponents.Scheme => _containers.TryGetValue(uri.Scheme, out _),
            UriComponents.Path => string.IsNullOrEmpty(uri.GetExtension()) || _wrappers.TryGetValue(uri.GetExtension()!, out _),
            _ => throw new NotImplementedException(),
        };

    public virtual bool CanHandle(Uri uri)
<<<<<<< HEAD
    {
        ArgumentNullException.ThrowIfNull(uri);
        return CanHandle(UriComponents.Scheme, uri) && CanHandle(UriComponents.Path, uri);
    }
=======
        => CanHandle(UriComponents.Scheme, uri) && CanHandle(UriComponents.Path, uri);
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
}
