using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Storage;

namespace Packata.Storages;
public class HttpDataPackageContainer : HttpStorageHandler, IDataPackageContainer
{
    public Uri BaseUri { get; }

    public HttpDataPackageContainer(Uri baseUri, HttpClient? httpClient = null)
        : base(httpClient)
    {
        BaseUri = baseUri.ToString().EndsWith('/')
            ? baseUri
            : new Uri(baseUri.ToString() + '/');
        _client.BaseAddress = BaseUri;
    }

    public override Task<Stream> OpenAsync(string relativePath)
        => OpenAsync(new Uri(BaseUri, relativePath));

    public override Task<bool> ExistsAsync(string relativePath)
        => ExistsAsync(new Uri(BaseUri, relativePath));
}
