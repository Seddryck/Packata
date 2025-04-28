using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stowage.Impl.Microsoft;
using Stowage;
using Packata.Core.Storage;
using System.Net.Sockets;

namespace Packata.Storages;
public class StorageProviderBuilder
{
    private readonly List<KeyValuePair<string, Func<IStorageHandler>>> _handlers = [];

    public StorageProviderBuilder Register(
        string scheme,
        Func<StorageHandlerBuilder, KeyValuePair<string, Func<IStorageHandler>>> builder)
        => Register([scheme], builder);

    public StorageProviderBuilder Register(
        string[] schemes,
        Func<StorageHandlerBuilder, KeyValuePair<string, Func<IStorageHandler>>> builder)
    {
        _handlers.AddRange(schemes.Select(scheme => builder(new StorageHandlerBuilder(scheme))));
        return this;
    }

    public IStorageProvider Build()
        => new StorageProvider(_handlers
            .GroupBy(b => b.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                if (group.Count() > 1)
                    throw new InvalidOperationException($"Scheme '{group.Key}' is registered multiple times.");
                return new { Scheme = group.Key, Storage = group.Single().Value };
            })
            .ToDictionary(x => x.Scheme, x => x.Storage.Invoke(), StringComparer.OrdinalIgnoreCase));

    public class StorageHandlerBuilder
    {
        private readonly string _scheme;

        internal StorageHandlerBuilder(string scheme)
            => _scheme = scheme;

        public KeyValuePair<string, Func<IStorageHandler>> UseHttp(HttpClient? client = null)
            => new(_scheme,
                () => new HttpStorageHandler(client ?? new HttpClient()));
    }
}
