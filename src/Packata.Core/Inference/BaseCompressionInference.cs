using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Inference;
public abstract class BaseCompressionInference : ICompressionInference
{
    public abstract bool TryInfer(Resource resource, out string? compression);

    protected bool TryInferFromExtension(string? extension, out string? compression)
    {
        compression = extension switch
        {
            "gz" or "gzip" => "gzip",
            "zip" => "zip",
            "zz" or "deflate" => "deflate",
            _ => null
        };
        return compression is not null;
    }
}
