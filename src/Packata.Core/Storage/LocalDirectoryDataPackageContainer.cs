using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
internal class LocalDirectoryDataPackageContainer : IDataPackageContainer
{
    private static readonly Uri _fileRoot = new UriBuilder
    {
        Scheme = Uri.UriSchemeFile,
        Host = string.Empty, // "localhost" if you leave this null/empty
        Path = "/"
    }.Uri;

    public Uri BaseUri { get; }

    public LocalDirectoryDataPackageContainer()
        => BaseUri = _fileRoot;

    public LocalDirectoryDataPackageContainer(Uri baseUri)
    {
        if (baseUri.ToString().EndsWith('/'))
            BaseUri = baseUri;
        BaseUri = new Uri(baseUri.ToString() + '/', UriKind.Absolute);
    }

    public void Dispose()
    { }

    public Task<bool> ExistsAsync(string relativePath)
    {
        var fullPath = new Uri(BaseUri, relativePath).LocalPath;
        return Task.FromResult(File.Exists(fullPath));
    }
    public Task<Stream> OpenAsync(string relativePath)
    {
        var fullPath = new Uri(BaseUri, relativePath).LocalPath;
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"'{relativePath}' not found in current directory.");
        return Task.FromResult((Stream)File.OpenRead(fullPath));
    }
}
