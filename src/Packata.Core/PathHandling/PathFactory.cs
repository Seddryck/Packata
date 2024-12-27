using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.PathHandling;
public class PathFactory
{
    public IPath Create(string path)
    {
        if (path.Contains("://"))
            return new HttpPath(new HttpClient(), path);
        return new LocalPath(Environment.CurrentDirectory, path);
    }
}
