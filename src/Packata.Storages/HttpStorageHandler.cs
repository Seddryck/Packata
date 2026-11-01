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
    private readonly CancellationToken _cancellationToken;
    /// <summary>The final response location after the client's redirect policy has been applied.</summary>
    /// <value>
    /// The final response location after the client's redirect policy has been applied.
    /// </value>
    public Uri? ResponseUri { get; private set; }

    public HttpStorageHandler(HttpClient? httpClient = null)
        : this(httpClient, CancellationToken.None) { }

    public HttpStorageHandler(HttpClient? httpClient, CancellationToken cancellationToken)
    {
        _disposeClient = httpClient is null;
        _client = httpClient ?? new HttpClient();
        _cancellationToken = cancellationToken;
    }

    public virtual Task<Stream> OpenAsync(string absolutePath)
        => OpenAsync(new Uri(absolutePath, UriKind.Absolute));

    protected async Task<Stream> OpenAsync(Uri uri)
    {
        ThrowIfDisposed();
        try
        {
            var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, _cancellationToken).ConfigureAwait(false);
            try
            {
                response.EnsureSuccessStatusCode();
                ResponseUri = response.RequestMessage?.RequestUri ?? uri;
                var stream = await response.Content.ReadAsStreamAsync(_cancellationToken).ConfigureAwait(false);
                return new BoundedReadStream(stream, SeekableStaging.DefaultMaximumBytes, _cancellationToken, response);
            }
            catch { response.Dispose(); throw; }
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
        using var request = new HttpRequestMessage(HttpMethod.Head, uri);
        using var response = await _client.SendAsync(request, _cancellationToken).ConfigureAwait(false);
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
