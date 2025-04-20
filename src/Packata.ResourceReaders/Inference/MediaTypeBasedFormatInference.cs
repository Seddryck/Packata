using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.ResourceReaders.Inference;
public class MediaTypeBasedFormatInference : IFormatInference
{
    public bool TryInfer(Resource resource, out string? format)
    {
        var mediaType = resource.MediaType;
        format = null;
        if (string.IsNullOrEmpty(mediaType))
            return false;

        // remove the charset value
        mediaType = mediaType.ToLowerInvariant().Split(';')[0].Split('+')[0];
        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            mediaType = mediaType.Substring("text/".Length);
            format = mediaType switch
            {
                "csv" => "csv",
                "tsv" => "tsv",
                "tab-separated-values" => "tsv",
                "psv" => "psv",
                _ => null
            };
        }
        else if (mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase))
        {
            mediaType = mediaType.Substring("application/".Length);
            format = mediaType switch
            {
                "vnd.ms-excel" => "xls",
                "vnd.apache.parquet" => "parquet",
                _ => null
            };
        }
        else
        {
            return false;
        }

        return format is not null;
    }
}
