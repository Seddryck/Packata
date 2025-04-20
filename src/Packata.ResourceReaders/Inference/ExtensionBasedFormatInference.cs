using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;

namespace Packata.ResourceReaders.Inference;
public class ExtensionBasedFormatInference : IFormatInference
{
    private readonly IExtractExtension _extractor;

    public ExtensionBasedFormatInference(IExtractExtension extractor)
    {
        _extractor = extractor;
    }

    public bool TryInfer(Resource resource, out string? format)
    {
        format = null;
        if (_extractor.TryGetPathExtension(resource.Paths.ToArray(), out var extension))
        {
            if (string.IsNullOrEmpty(extension))
                return false;
            var blocks = extension.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (blocks.Length > 2)
                return false;
            format = blocks[0] switch
            {
                "csv" => "csv",
                "tsv" => "tsv",
                "psv" => "psv",
                "xls" or "xlsx" => "xls",
                "parquet" or "pqt" => "parquet",
                _ => null
            };
            return format is not null;
        }
        return false;
    }
}
