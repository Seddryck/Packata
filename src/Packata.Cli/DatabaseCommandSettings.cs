using System.ComponentModel;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal abstract class DatabaseCommandSettings : CommandSettings
{
    [CommandOption("--connector <SCHEME=ID>")]
    [Description("Override a connector preference for this invocation; repeatable.")]
    public string[] Connectors { get; init; } = [];

    [CommandOption("--connection-env <ENDPOINT=VARIABLE>")]
    [Description("Read a runtime endpoint connection URL from an environment variable; repeatable.")]
    public string[] ConnectionEnvironment { get; init; } = [];
}
