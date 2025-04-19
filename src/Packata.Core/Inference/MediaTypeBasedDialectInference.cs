using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.Inference;
public class MediaTypeBasedDialectInference : FormatBasedDialectInference
{
    public override bool TryInfer(Resource resource, out TableDelimitedDialect? dialect)
    {
        var mediaType = resource.MediaType;
        dialect = null;
        if (string.IsNullOrEmpty(mediaType))
            return false;

        if (!mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return false;

        mediaType = mediaType.ToLowerInvariant().Substring(5).Split([';'])[0];
        var format = mediaType switch
        {
            "csv" => "csv",
            "tsv" => "tsv",
            "tab-separated-values" => "tsv",
            "psv" => "psv",
            _ => null
        };

        return TryInferFromFormat(format, out dialect);
    }
}
