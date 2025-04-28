using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Storage;

namespace Packata.Storages;
public class HttpDataPackageContainer : IDataPackageContainer
{
    private readonly HttpClient _client;
    private readonly bool _disposeClient;

    public Uri BaseUri { get; }

    public HttpDataPackageContainer(Uri baseUri, HttpClient? httpClient = null)
    {
<<<<<<< HEAD
        BaseUri = baseUri.ToString().EndsWith('/')
            ? baseUri
            : new Uri(baseUri.ToString() + '/');
=======
        BaseUri = baseUri;
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
        _disposeClient = httpClient is null;
        _client = httpClient ?? new HttpClient();
        _client.BaseAddress = BaseUri;
    }

<<<<<<< HEAD
    public async Task<Stream> OpenAsync(string relativePath)
    {
        ThrowIfDisposed();
        var resolved = new Uri(BaseUri, relativePath);
        try
        {
            return await _client.GetStreamAsync(resolved);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException($"'{relativePath}' not found at '{resolved}'.", ex);
        }
=======
    public Task<Stream> OpenAsync(string relativePath)
    {
        ThrowIfDisposed();
        var resolved = new Uri(BaseUri, relativePath);
        return _client.GetStreamAsync(resolved);
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
    }

    public async Task<bool> ExistsAsync(string relativePath)
    {
        ThrowIfDisposed();
        var url = new Uri(BaseUri, relativePath);
        var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = await _client.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private bool _disposed = false;
<<<<<<< HEAD

=======
    
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, nameof(HttpDataPackageContainer));

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                if (_disposeClient)
                {
                    _client.Dispose();
                }
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    ~HttpDataPackageContainer()
    {
        Dispose(disposing: false);
    }
}
