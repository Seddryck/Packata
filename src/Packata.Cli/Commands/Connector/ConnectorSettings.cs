using System.ComponentModel;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class ConnectorListSettings : PresentationSettings
{
    [CommandOption("--scheme <SCHEME>")]
    [Description("Filter connectors by database connection scheme.")]
    public string? Scheme { get; init; }

    [CommandOption("--verbose")]
    [Description("Show connector aliases, compatibility, discovery facts and prerequisites.")]
    public bool Verbose { get; init; }
}
internal sealed class ConnectorShowSettings : PresentationSettings
{
    [CommandArgument(0, "<ID>")]
    public required string Id { get; init; }
}
internal sealed class ConnectorInstallSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    public required string Id { get; init; }
}
