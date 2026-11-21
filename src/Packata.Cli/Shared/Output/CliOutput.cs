using System.Text.Json;

namespace Packata.Cli;

internal static class CliOutput
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static void Json(object value, TextWriter? writer = null) => (writer ?? Console.Out).WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    public static int Run(Action action)
    {
        try { action(); return 0; }
        catch (Exception error) { return Error(error); }
    }
    public static int Error(Exception error)
    {
        Console.Error.WriteLine(error is CliException ? error.Message : "The operation failed. Check configuration, permissions and connector prerequisites.");
        return 1;
    }
    public static void Configuration(IEnumerable<ConfigurationValue> values, string format, TextWriter? writer = null)
    {
        writer ??= Console.Out;
        if (format == "json") { Json(values, writer); return; }
        foreach (var value in values)
        {
            var effective = value.BuiltInValue is null ? value.Value : FormatSelection(value.Value);
            var builtIn = value.BuiltInValue is null ? "" : $" [built-in: {FormatSelection(value.BuiltInValue)}]";
            writer.WriteLine($"{value.Key} = {effective} ({value.Origin}){builtIn}" +
                (value.Origin.StartsWith("environment:", StringComparison.Ordinal) && value.UserValue is not null
                    ? $" [user override masked: {value.UserValue}]" : ""));
        }
    }

    private static string FormatSelection(object value) => value is string text ? text : JsonSerializer.Serialize(value);
}
