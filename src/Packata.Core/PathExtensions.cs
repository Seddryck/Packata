using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public static class PathExtensions
{
    public static string[] GetAllExtensions(this string filename)
    {
        var name = Path.GetFileName(filename);
        var parts = name.Split('.');
        if (parts.Length <= 1)
            return Array.Empty<string>();

        return parts.Skip(1).ToArray();
    }

    public static string GetLongExtension(this string filename)
        => string.Join('.', GetAllExtensions(filename));
}
