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
        AddMapping("year", "%Y");
        AddMapping("yearmonth", "%Y-%m");
    }

    private Dictionary<string, string> Mappings { get; } = [];
    public void AddMapping(string type, string defaultFormat)
    {
        if (!Mappings.TryAdd(type, defaultFormat))
            Mappings[type] = defaultFormat;
    }
    public bool TryGetMapping(string type, [NotNullWhen(true)] out string? format)
        => Mappings.TryGetValue(type, out format);
}
