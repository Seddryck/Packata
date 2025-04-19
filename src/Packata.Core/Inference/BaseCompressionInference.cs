using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Inference;
public class BaseCompressionInference
{
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
