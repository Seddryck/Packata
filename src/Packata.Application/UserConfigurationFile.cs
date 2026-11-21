using System.Text.Json;

namespace Packata.Application;

internal static class UserConfigurationFile
{
    internal static string PathFor(Func<string, string?> environment)
    {
        var home = environment("PACKATA_CONFIG_HOME");
        return Path.Combine(string.IsNullOrWhiteSpace(home)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Packata") : home,
            "config.json");
    }

    internal static Dictionary<string, string> ReadMappings(string path)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return settings;
        var text = File.ReadAllText(path);
        // Older file workflows ignored connector configuration. Read mapping keys only from a settings object.
        if (!text.AsSpan().TrimStart().StartsWith("{", StringComparison.Ordinal)) return settings;
        try
        {
            using var document = JsonDocument.Parse(text);
            foreach (var entry in document.RootElement.EnumerateObject().Where(entry => MappingConfiguration.IsMappingKey(entry.Name)))
            {
                if (entry.Value.ValueKind != JsonValueKind.String)
                    throw new FormatException($"'{entry.Name}' requires a string configuration value.");
                settings[entry.Name] = entry.Value.GetString()!;
            }
            return settings;
        }
        catch (JsonException exception)
        {
            throw new FormatException("The user configuration is invalid JSON.", exception);
        }
    }

    internal static Dictionary<string, string> Read(string path)
    {
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                ?? throw new FormatException("The user configuration is empty.");
        }
        catch (JsonException exception)
        {
            throw new FormatException("The user configuration is invalid JSON.", exception);
        }
    }
}
