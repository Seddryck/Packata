using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class RootCommand : Command<RootCommand.Settings>
{
    internal sealed class Settings : CommandSettings;

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("Packata command-line interface. Run [blue]packata --help[/] for usage.");
        return 0;
    }
}
