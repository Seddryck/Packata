using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.ResourceReaders.Inference;
public class ExtensionBasedDialectInference : FormatBasedDialectInference
{
    private readonly IExtractExtension _extractor;

    public ExtensionBasedDialectInference(IExtractExtension extractor)
    {
        _extractor = extractor;
    }

    public override bool TryInfer(Resource resource, [NotNullWhen(true)] out TableDialect? dialect)
    {
        dialect = null;
        if (_extractor.TryGetPathExtension(resource.Paths.ToArray(), out var extension))
        {
            if (string.IsNullOrEmpty(extension))
                return false;
            var blocks = extension.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (blocks.Length > 2)
                return false;
            return TryInferFromFormat(blocks[0], out dialect);
        }
        return false;
    }
}
