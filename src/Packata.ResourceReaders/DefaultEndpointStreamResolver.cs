using Packata.Core.Reading;

namespace Packata.ResourceReaders;

public sealed class DefaultEndpointStreamResolver : IEndpointStreamResolver
{
    private static readonly HttpClient Http = new();

    public async ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return await Http.GetStreamAsync(uri, cancellationToken).ConfigureAwait(false);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
    }
}
