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
        AbsolutePath = fullyQualifiedPath;
        _provider = provider;
    }

    public Task<Stream> OpenAsync()
        => _provider.OpenAsync(AbsolutePath);
    public Task<bool> ExistsAsync()
        => _provider.ExistsAsync(AbsolutePath);

    public override string ToString() => AbsolutePath;
}
