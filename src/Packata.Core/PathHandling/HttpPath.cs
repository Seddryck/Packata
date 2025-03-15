using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.PathHandling;
public class HttpPath : IPath
{
    public HttpClient Client { get; }
    public string Path { get; }

    public HttpPath(HttpClient client, string path)
        => (Client, Path) = (client, path);

    public bool Exists()
        => Exists(Client.GetAsync(Path).Result);

    private bool Exists(HttpResponseMessage response)
        => response.IsSuccessStatusCode;

    public Stream ToStream()
    {
        var response = Client.GetAsync(Path).Result;
        if (!Exists(response))
            throw new FileNotFoundException($"The file '{Path}' does not exist.");
        return response.Content.ReadAsStream();
    }

    #region Equality
    public override string ToString()
        => Path;
    public override bool Equals(object? obj)
        => obj is HttpPath path && Path == path.Path;
    public override int GetHashCode()
        => HashCode.Combine(Path);
    #endregion
}
