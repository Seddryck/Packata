using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Storage;

namespace Packata.Storages;
public class HttpStorageHandler : IStorageHandler
{
    protected readonly HttpClient _client;
    private readonly bool _disposeClient;

    public HttpStorageHandler(HttpClient? httpClient = null)
    {
        _disposeClient = httpClient is null;
        _client = httpClient ?? new HttpClient();
    }

    public virtual Task<Stream> OpenAsync(string absolutePath)
        => OpenAsync(new Uri(absolutePath, UriKind.Absolute));

    protected async Task<Stream> OpenAsync(Uri uri)
    {
        ThrowIfDisposed();
        try
        {
            return await _client.GetStreamAsync(uri);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException($"'{uri}' not found.", ex);
        }
    }

    public virtual Task<bool> ExistsAsync(string absolutePath)
        => ExistsAsync(new Uri(absolutePath, UriKind.Absolute));

    protected async Task<bool> ExistsAsync(Uri uri)
    {
        ThrowIfDisposed();
        var request = new HttpRequestMessage(HttpMethod.Head, uri);
        using var response = await _client.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private bool _disposed = false;
    protected virtual void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, nameof(HttpStorageHandler));

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

    ~HttpStorageHandler()
    {
        Dispose(disposing: false);
    }
}
