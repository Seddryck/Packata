using Packata.Core.Reading;

namespace Packata.ResourceReaders;

public sealed class DefaultEndpointStreamResolver : IEndpointStreamResolver
{
    private static readonly HttpClient Http = new();

    public async ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
                return await Http.GetStreamAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!uri.IsFile) throw new NotSupportedException("Unsupported resource scheme; expected file, HTTP or HTTPS.");
            path = uri.LocalPath;
        }
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
    }
}
