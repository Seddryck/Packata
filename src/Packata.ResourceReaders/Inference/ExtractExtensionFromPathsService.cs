using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core;
using Packata.Core.PathHandling;
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

        var pathTypes = paths.Select(p => p.GetType()).Distinct();

        if (pathTypes.Count() != 1)
            return false;

        if (pathTypes.First() == typeof(HttpPath))
        {
            try
            {
                var files = paths.Where(p => p is HttpPath)
                                    .Select(http =>
                                    {
                                        try { return new Uri(http.ToString() ?? string.Empty).Segments.LastOrDefault(); }
                                        catch (UriFormatException) { return null; }
                                    })
                                .Where(file => !string.IsNullOrEmpty(file))
                                .Distinct();
                if (files.Count() != 1)
                    return false;
                var ext = System.IO.Path.GetExtension(files.First());
                extension = !string.IsNullOrEmpty(ext) ? ext.ToLowerInvariant().TrimStart('.') : null;
            }
            catch (Exception)
            {
                return false;
            }
        }
        else
        {
            var extensions = paths.Select(p => System.IO.Path.GetExtension(p.ToString() ?? string.Empty))
                                            .Where(ext => !string.IsNullOrEmpty(ext))
                                            .Select(ext => ext.ToLowerInvariant().TrimStart('.'))
                                            .Distinct();
            if (extensions.Count() != 1)
                return false;
            extension = extensions.First();
        }
        return !string.IsNullOrEmpty(extension);
    }
}
