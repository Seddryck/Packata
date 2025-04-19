using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.ResourceReaders.Inference;
public class ExtensionBasedCompressionInference : BaseCompressionInference
{
    private readonly IExtractExtension _extractor;

    public ExtensionBasedCompressionInference(IExtractExtension extractor, IDictionary<string, string> compressionMappings)
        : base(compressionMappings)
    {
        _extractor = extractor;
    }

    public override bool TryInfer(Resource resource, out string? compression)
    {
        compression = null;
        if (resource == null || resource.Paths == null || !resource.Paths.Any())
            return false;
        
        if (_extractor.TryGetPathExtension(resource.Paths.ToArray(), out var extension))
        {
            if (string.IsNullOrEmpty(extension))
                return false;
            var blocks = extension.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (blocks.Length < 1 || blocks.Length > 2)
                return false;
            return TryInferFromExtension(blocks.Last(), out compression);
        }
        return false;
    }
}
