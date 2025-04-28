using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
internal class StorageProvider : IStorageProvider
{
    private readonly Dictionary<string, IStorageHandler> _handlers = [];

    public void Register(string scheme, IStorageHandler handler)
    {
        if (string.IsNullOrEmpty(scheme))
            throw new ArgumentException("Scheme cannot be null or empty", nameof(scheme));

        _handlers[scheme] = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public bool CanHandle(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return false;

        var scheme = new Uri(absolutePath).Scheme;
        return _handlers.ContainsKey(scheme);
    }
    public Task<bool> ExistsAsync(string absolutePath)
    {
        var scheme = new Uri(absolutePath).Scheme;
        if (_handlers.TryGetValue(scheme, out var handler))
            return handler.ExistsAsync(absolutePath);
        throw new NotSupportedException($"The scheme '{scheme}' is not supported for a fully qualified path.");
    }
    public Task<Stream> OpenAsync(string absolutePath)
    {
        var scheme = new Uri(absolutePath).Scheme;
        if (_handlers.TryGetValue(scheme, out var handler))
            return handler.OpenAsync(absolutePath);
        throw new NotSupportedException($"The scheme '{scheme}' is not supported for a fully qualified path.");
    }
}
