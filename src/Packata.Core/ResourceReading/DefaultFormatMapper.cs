using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core.ResourceReading;
public class DefaultFormatMapper
{
    public DefaultFormatMapper()
    {
        AddMapping("datetime", "%Y-%m-%dT%H:%M:%S");
        AddMapping("date", "%Y-%m-%d");
        AddMapping("time", "%H:%M:%S");
    }

    private Dictionary<string, string> _mappings = [];
    public void AddMapping(string type, string defaultFormat)
    {
        if (!_mappings.TryAdd(type, defaultFormat))
            _mappings[type] = defaultFormat;
    }
    public bool TryGetMapping(string type, [NotNullWhen(true)] out string? format)
        => _mappings.TryGetValue(type, out format);
}
