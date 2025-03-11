using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.Testing.PathHandling;

namespace Packata.Core.PathHandling;
internal class LocalPath : IPath
{
    public string Root { get; }
    public string Path { get; }

    private IFileSystem FileSystem { get; }

    public string FullPath
        => System.IO.Path.Combine(Root, Path);

    public LocalPath(string root, string path)
        : this(new FileSystem(), root, path) { }

    protected internal LocalPath(IFileSystem fileSystem, string root, string path)
        => (FileSystem, Root, Path) = (fileSystem, root, path);

    public bool Exists()
        => FileSystem.Exists(FullPath);

    public Stream ToStream()
    {
        if (!Exists())
            throw new FileNotFoundException($"The file '{Path}' does not exist.");
        return FileSystem.OpenRead(FullPath);
    }

    #region Equality
    public override string ToString()
        => Path;
    public override bool Equals(object? obj)
        => obj is LocalPath path && Root == path.Root && Path == path.Path;
    public override int GetHashCode()
        => HashCode.Combine(Root, Path);
    #endregion
}
