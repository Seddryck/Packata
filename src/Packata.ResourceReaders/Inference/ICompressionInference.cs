using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.ResourceReaders.Inference;
public interface ICompressionInference
{
    bool TryInfer(Resource resource, out string? compression);
}
