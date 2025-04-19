using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Inference;
public interface IExtractExtension
{
    bool TryGetPathExtension(IPath[]? paths, out string? extension);
}
