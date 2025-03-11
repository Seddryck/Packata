using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.PathHandling;
public class PathFactory
{
    private readonly HttpClient httpClient = new();

    public IPath Create(string path)
    {
        if (IsUrl(path, out _))
            return new HttpPath(httpClient, path);
        return new LocalPath(Environment.CurrentDirectory, path);
    }

    private static bool IsUrl(string path, [NotNullWhen(true)] out Uri? url)
        => Uri.TryCreate(path, UriKind.Absolute, out url) &&
               (url.Scheme == Uri.UriSchemeHttp ||
                url.Scheme == Uri.UriSchemeHttps);
}
