using Spectre.Console.Cli;

namespace Packata.Cli;

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
