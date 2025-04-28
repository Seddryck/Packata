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
public class DataPackageLocatorBuilder
{
    private readonly List<KeyValuePair<string, Func<Uri, IDataPackageContainer>>> _containers = [];
    private readonly List<KeyValuePair<string, Func<Uri, IContainerWrapper>>> _wrappers = [];

    public DataPackageLocatorBuilder Register(
        string scheme,
        Func<ContainerBuilder, KeyValuePair<string, Func<Uri, IDataPackageContainer>>> builder)
        => Register([scheme], builder);

    public DataPackageLocatorBuilder Register(
        string[] schemes,
        Func<ContainerBuilder, KeyValuePair<string, Func<Uri, IDataPackageContainer>>> builder)
    {
        _containers.AddRange(schemes.Select(scheme => builder(new ContainerBuilder(scheme))));
        return this;
    }

    public DataPackageLocatorBuilder Register(
        string extension,
        Func<WrapperBuilder, KeyValuePair<string, Func<Uri, IContainerWrapper>>> builder)
        => Register([extension], builder);

    public DataPackageLocatorBuilder Register(
        string[] extensions,
        Func<WrapperBuilder, KeyValuePair<string, Func<Uri, IContainerWrapper>>> builder)
    {
        _wrappers.AddRange(extensions.Select(extension => builder(new WrapperBuilder(extension))));
        return this;
    }

    public IDataPackageLocator Build()
        => new DataPackageLocator(_containers
            .GroupBy(b => b.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                if (group.Count() > 1)
                    throw new InvalidOperationException($"Scheme '{group.Key}' is registered multiple times.");
                return new { Scheme = group.Key, Storage = group.Single().Value };
            })
            .ToDictionary(x => x.Scheme, x => x.Storage, StringComparer.OrdinalIgnoreCase),
            _wrappers
            .GroupBy(b => b.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                if (group.Count() > 1)
                    throw new InvalidOperationException($"Extension '{group.Key}' is registered multiple times.");
                return new { Extension = group.Key, Wrapper = group.Single().Value };
            })
            .ToDictionary(x => x.Extension, x => x.Wrapper, StringComparer.OrdinalIgnoreCase));

    public interface IAzureBuilder
    {
        KeyValuePair<string, Func<Uri, IDataPackageContainer>> WithSharedKey(string sharedKey);
        KeyValuePair<string, Func<Uri, IDataPackageContainer>> WithClientSecret(string tenantId, string clientId, string clientSecret);
    }

    public class ContainerBuilder
    {
        private readonly string _scheme;

        internal ContainerBuilder(string scheme)
            => _scheme = scheme;

        public KeyValuePair<string, Func<Uri, IDataPackageContainer>> UseLocalFileSystem()
            => new(_scheme,
                uri =>
                {
                    var path = Path.GetFullPath(Uri.UnescapeDataString(uri.AbsolutePath));
                    var store = Files.Of.LocalDisk(path);
                    return new StowageDataPackageContainer(uri, store);
                });

        public KeyValuePair<string, Func<Uri, IDataPackageContainer>> UseHttp(HttpClient client)
            => new(_scheme,
                (uri) => new HttpDataPackageContainer(uri, client));

        public IAzureBuilder UseAzure(string accountName)
            => new AzureBuilder(_scheme, accountName);

        public KeyValuePair<string, Func<Uri, IDataPackageContainer>> UseAws(string accessKeyId, string secretAccessKey, string region)
            => new(_scheme,
                (uri) => new StowageDataPackageContainer(uri, Files.Of.AmazonS3(accessKeyId, secretAccessKey, region)));

        public class AzureBuilder : IAzureBuilder
        {
            private readonly string _scheme;
            private readonly string _accountName;

            public AzureBuilder(string scheme, string accountName)
                => (_scheme, _accountName) = (scheme, accountName);

            public KeyValuePair<string, Func<Uri,IDataPackageContainer>> WithSharedKey(string sharedKey)
                => new (_scheme,
                    (uri) => new StowageDataPackageContainer(uri,
                        Files.Of.AzureBlobStorage(_accountName, sharedKey)));

            public KeyValuePair<string, Func<Uri, IDataPackageContainer>> WithClientSecret(string tenantId, string clientId, string clientSecret)
                => new(_scheme, uri =>
                {
                    try
                    {
                        var storage = Files.Of.AzureBlobStorage(
                            _accountName,
                            new ClientSecretCredential(tenantId, clientId, clientSecret));
                        return new StowageDataPackageContainer(uri, storage);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Failed to create Blob storage for account '{_accountName}'.Please verify your tenant, client ID, and secret.", ex);
                    }
                });
        }
    }

    public class WrapperBuilder
    {
        private readonly string _extension;

        internal WrapperBuilder(string extension)
            => _extension = extension;

        public KeyValuePair<string, Func<Uri, IContainerWrapper>> UseZipArchive()
            => new(_extension.StartsWith('.') ? _extension : '.' + _extension,
                uri =>
                {
                    return new ZipContainerWrapper();
                });
    }
}
