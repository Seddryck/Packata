﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Testing.PathHandling;
internal class FileSystem : IFileSystem
{
    public bool Exists(string path)
        => File.Exists(path);
    public Stream OpenRead(string path)
        => File.OpenRead(path);
}
