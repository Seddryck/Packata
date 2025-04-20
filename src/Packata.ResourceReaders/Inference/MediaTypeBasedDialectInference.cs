using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.ResourceReaders.Inference;
public class MediaTypeBasedDialectInference : FormatBasedDialectInference
{
    public override bool TryInfer(Resource resource, out TableDelimitedDialect? dialect)
    {
        var mediaType = resource.MediaType;
        dialect = null;
        if (string.IsNullOrEmpty(mediaType))
            return false;

        string? format;
        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            mediaType = mediaType.ToLowerInvariant().Substring(5).Split([';'])[0];
            format = mediaType switch
            {
                "csv" => "csv",
                "tsv" => "tsv",
                "tab-separated-values" => "tsv",
                "psv" => "psv",
                _ => null
            };
        }
        else
        {
            return false;
        }

        return TryInferFromFormat(format, out dialect);
    }
}
