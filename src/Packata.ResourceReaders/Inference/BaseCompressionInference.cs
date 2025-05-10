using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.ResourceReaders.Inference;
public abstract class BaseCompressionInference : ICompressionInference
{
    protected IDictionary<string, string> CompressionMappings { get; }

    protected BaseCompressionInference(IDictionary<string, string> compressionMappings)
        => CompressionMappings = compressionMappings;

    public abstract bool TryInfer(Resource resource, [NotNullWhen(true)] out string? compression);

    protected bool TryInferFromExtension(string? extension, out string? compression)
    {
        if (string.IsNullOrEmpty(extension))
        {
            compression = null;
            return false;
        }
        return CompressionMappings.TryGetValue(extension, out compression);
    }

        
}
