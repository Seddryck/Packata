using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Storage;
public class FullyQualifiedPath : IPath
{
    public string AbsolutePath { get; }
    public string Value => AbsolutePath;
    private readonly IStorageProvider _provider;

    public bool IsFullyQualified => true;

    public FullyQualifiedPath(string fullyQualifiedPath, IStorageProvider provider)
    {
        if (string.IsNullOrEmpty(fullyQualifiedPath))
            throw new ArgumentException("Path cannot be null or empty", nameof(fullyQualifiedPath));

        // Ensure the path can be parsed as a URI with a scheme
        try
        {
            var uri = new Uri(fullyQualifiedPath);
            if (string.IsNullOrEmpty(uri.Scheme))
                throw new ArgumentException("Path must have a URI scheme", nameof(fullyQualifiedPath));
        }
        catch (UriFormatException ex)
        {
            throw new ArgumentException("Path must be a valid URI", nameof(fullyQualifiedPath), ex);
        }

        AbsolutePath = fullyQualifiedPath;
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public Task<Stream> OpenAsync()
        => _provider.OpenAsync(AbsolutePath);
    public Task<bool> ExistsAsync()
        => _provider.ExistsAsync(AbsolutePath);

    public override string ToString() => AbsolutePath;
}
