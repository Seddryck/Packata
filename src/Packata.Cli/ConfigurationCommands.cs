using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal class PresentationSettings : CommandSettings
{
    [CommandOption("--output-format <FORMAT>")]
    [Description("Presentation format: text or json (default text).")]
    public string OutputFormat { get; init; } = "text";
    public override ValidationResult Validate() => OutputFormat is "text" or "json" ? ValidationResult.Success()
        : ValidationResult.Error("--output-format must be text or json.");
}

internal sealed class ConfigGetSettings : PresentationSettings
{
    [CommandArgument(0, "<KEY>")]
    public required string Key { get; init; }
}

internal class ConfigKeySettings : CommandSettings
{
    [CommandArgument(0, "<KEY>")]
    public required string Key { get; init; }
}

internal sealed class ConfigSetSettings : ConfigKeySettings
{
    [CommandArgument(1, "<VALUE>")]
    public required string Value { get; init; }
}

internal static class CliOutput
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static void Json(object value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
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
    public static void Configuration(IEnumerable<ConfigurationValue> values, string format)
    {
        if (format == "json") { Json(values); return; }
        foreach (var value in values)
            Console.WriteLine($"{value.Key} = {value.Value} ({value.Origin})" +
                (value.Origin.StartsWith("environment:", StringComparison.Ordinal) && value.UserValue is not null
                    ? $" [user override masked: {value.UserValue}]" : ""));
    }
}

internal sealed class ConfigListCommand : Command<PresentationSettings>
{
    public override int Execute(CommandContext context, PresentationSettings settings, CancellationToken cancellationToken)
        => CliOutput.Run(() => CliOutput.Configuration(new CliConfiguration().List(), settings.OutputFormat));
}
internal sealed class ConfigGetCommand : Command<ConfigGetSettings>
{
    public override int Execute(CommandContext context, ConfigGetSettings settings, CancellationToken cancellationToken)
        => CliOutput.Run(() => CliOutput.Configuration([new CliConfiguration().Get(settings.Key)], settings.OutputFormat));
}
internal sealed class ConfigSetCommand : Command<ConfigSetSettings>
{
    public override int Execute(CommandContext context, ConfigSetSettings settings, CancellationToken cancellationToken)
        => CliOutput.Run(() => new CliConfiguration().Set(settings.Key, settings.Value));
}
internal sealed class ConfigUnsetCommand : Command<ConfigKeySettings>
{
    public override int Execute(CommandContext context, ConfigKeySettings settings, CancellationToken cancellationToken)
        => CliOutput.Run(() => new CliConfiguration().Unset(settings.Key));
}
