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
public class DataPackageLocator : IDocumentLocator
{
    private readonly IDictionary<string, Func<Uri, IDocumentContainer>> _containers;
    private readonly IDictionary<string, Func<Uri, IContainerWrapper>> _wrappers;

    protected internal DataPackageLocator(
        IDictionary<string, Func<Uri, IDocumentContainer>> containers,
        IDictionary<string, Func<Uri, IContainerWrapper>> wrappers)
    {
        (_containers, _wrappers) = (containers, wrappers);
    }

    public async Task<DocumentHandle> LocateAsync(Uri containerUri, string descriptorName = "datapackage.json")
    {
        ArgumentNullException.ThrowIfNull(containerUri);
        var suffix = FindWrapperSuffix(containerUri);
        if (suffix is null)
        {
            if (!CanHandle(containerUri))
                throw new NotSupportedException($"No container found for URI: {containerUri}");
            return new DocumentHandle(GetContainer(containerUri), descriptorName);
        }

        var source = GetContainer(new Uri(containerUri, "."));
        try
        {
            var archive = await GetWrapper(containerUri).WrapAsync(source, containerUri).ConfigureAwait(false);
            return new DocumentHandle(new OwnedArchiveContainer(archive, source), descriptorName);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    protected internal virtual IDocumentContainer GetContainer(Uri uri)
    {
        if (_containers.TryGetValue(uri.Scheme, out var storage))
            return storage.Invoke(uri);
        throw new NotSupportedException($"No container found for URI: {uri}");
    }

    protected internal virtual IContainerWrapper GetWrapper(Uri uri)
    {
        if (_wrappers.TryGetValue(FindWrapperSuffix(uri) ?? string.Empty, out var storage))
            return storage.Invoke(uri);
        throw new NotSupportedException($"No wrapper found for URI: {uri}");
    }

    protected virtual bool CanHandle(UriComponents component, Uri uri)
        => component switch
        {
            UriComponents.Scheme => _containers.TryGetValue(uri.Scheme, out _),
            UriComponents.Path => string.IsNullOrEmpty(uri.GetExtension()) || FindWrapperSuffix(uri) is not null,
            _ => throw new NotImplementedException(),
        };

    public virtual bool CanHandle(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return CanHandle(UriComponents.Scheme, uri) && CanHandle(UriComponents.Path, uri);
    }

    private string? FindWrapperSuffix(Uri uri) => _wrappers.Keys
        .Where(suffix => uri.AbsolutePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(suffix => suffix.Length)
        .FirstOrDefault();
}
