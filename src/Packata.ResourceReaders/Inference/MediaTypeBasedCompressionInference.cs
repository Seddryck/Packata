using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.ResourceReaders.Inference;
public class MediaTypeBasedCompressionInference : BaseCompressionInference
{
    public MediaTypeBasedCompressionInference(IDictionary<string, string> compressionMappings)
        : base(compressionMappings)
    { }

    public override bool TryInfer(Resource resource, out string? compression)
    {
        var mediaType = resource.MediaType;
        compression = null;
        if (string.IsNullOrEmpty(mediaType))
            return false;

        if (!mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase))
            return false;

        mediaType = mediaType.ToLowerInvariant().Substring("application/".Length).Split([';'])[0];
        var extension = mediaType switch
        {
            "x-deflate" => "deflate",
            _ => mediaType
        };

        return TryInferFromExtension(extension, out compression);
    }
}
