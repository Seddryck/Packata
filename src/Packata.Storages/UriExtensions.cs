﻿using System;
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
        var lastDotIndex = lastSegment.LastIndexOf('.');
        if (lastDotIndex >= 0)
            return lastSegment.Substring(lastDotIndex);
        return null;
    }
}
