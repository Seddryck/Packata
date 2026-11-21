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
