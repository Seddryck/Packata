using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Testing.PathHandling;
public interface IFileSystem
{
    bool Exists(string path);
    Stream OpenRead(string path);
}
