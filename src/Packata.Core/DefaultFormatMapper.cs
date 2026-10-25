namespace Packata.Core;

/// <summary>Default strptime patterns for temporal fields.</summary>
public class DefaultFormatMapper
{
    private readonly Dictionary<string, string> _mappings = new()
    {
        ["datetime"] = "%Y-%m-%dT%H:%M:%S", ["date"] = "%Y-%m-%d", ["time"] = "%H:%M:%S",
        ["year"] = "%Y", ["yearmonth"] = "%Y-%m"
    };

    public void AddMapping(string type, string defaultFormat) => _mappings[type] = defaultFormat;

    public bool TryGetMapping(string type, out string? format) => _mappings.TryGetValue(type, out format);
}
