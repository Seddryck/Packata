using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.PathHandling;

namespace Packata.Core.Inference;
internal class ExtractExtensionFromPathsService : IExtractExtension
{
    public bool TryGetPathExtension(IPath[]? paths, out string? extension)
    {
        extension = null;
        if (paths == null || paths.Length == 0)
            return false;

        var pathTypes = paths.Select(p => p.GetType()).Distinct();

        if (pathTypes.Count() != 1)
            return false;

        if (pathTypes.First() == typeof(HttpPath))
        {
            var files = paths.Where(p => p is HttpPath)
                            .Select(http => new Uri(http.ToString()!).Segments.LastOrDefault())
                            .Where(file => !string.IsNullOrEmpty(file))
                            .Distinct();
            if (files.Count() != 1)
                return false;
            extension = System.IO.Path.GetExtension(files.First())?.ToLowerInvariant().Substring(1);
        }
        else
        {
            var extensions = paths.Select(p => System.IO.Path.GetExtension(p.ToString()!).ToLowerInvariant()).Distinct();
            if (extensions.Count() != 1)
                return false;
            extension = extensions.First().Substring(1);
        }
        return !string.IsNullOrEmpty(extension);
    }
}
