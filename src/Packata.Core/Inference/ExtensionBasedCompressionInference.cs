using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Inference;
public class ExtensionBasedCompressionInference : BaseCompressionInference, ICompressionInference
{
    private readonly IExtractExtension _extractor;

    public ExtensionBasedCompressionInference(IExtractExtension extractor)
    {
        _extractor = extractor;
    }

    public bool TryInfer(Resource resource, out string? compression)
    {
        compression = null;
        if (_extractor.TryGetPathExtension(resource.Paths.ToArray(), out var extension))
        {
            if (string.IsNullOrEmpty(extension))
                return false;
            var blocks = extension.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (blocks.Length!=2)
                return false;
            return TryInferFromExtension(blocks[1], out compression);
        }
        return false;
    }
}
