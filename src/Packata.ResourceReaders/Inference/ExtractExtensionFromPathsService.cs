using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;
using Packata.Core.Storage;
using static System.Net.Mime.MediaTypeNames;
using static System.Net.WebRequestMethods;

namespace Packata.ResourceReaders.Inference;
internal class ExtractExtensionFromPathsService : IExtractExtension
{
    public bool TryGetPathExtension(IPath[]? paths, out string? extension)
    {
        extension = null;
        if (paths == null || paths.Length == 0)
            return false;

        var hasRemote = paths.Any(p => p.RelativePath.Contains("://"));
        var hasLocal = paths.Any(p => !p.RelativePath.Contains("://"));

        if (hasRemote && hasLocal)
            return false;

        var extensions = paths.Select(p => (p.RelativePath ?? string.Empty).GetLongExtension())
                                        .Where(ext => !string.IsNullOrEmpty(ext))
                                        .Select(ext => ext.ToLowerInvariant().TrimStart('.'))
                                        .Distinct();
        if (extensions.Count() != 1)
            return false;
        extension = extensions.First();

        return !string.IsNullOrEmpty(extension);
    }
}
