using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Storages;
internal static class UriExtensions
{
    public static string? GetExtension(this Uri uri)
    {
        if (uri.Segments.Length == 0)
            return null;
        var lastSegment = uri.Segments[^1];
        if (lastSegment.Contains('.'))
<<<<<<< HEAD
            return '.' + lastSegment.Split('.').Last();
=======
            return lastSegment.Split('.').Last();
>>>>>>> b54efe1b3ea41dc884834ea15bf6d4852c1550cb
        return null;
    }
}
