using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public interface IPath
{
    public bool Exists();
    public Stream ToStream();
}
