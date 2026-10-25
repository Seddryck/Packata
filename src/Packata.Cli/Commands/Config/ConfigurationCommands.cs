using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class ConfigListCommand : Command<PresentationSettings>
{
    public override int Execute(CommandContext context, PresentationSettings settings, CancellationToken cancellationToken)
        => CliOutput.Run(() =>
        {
            using var output = new OutputDestination(settings.Output);
            CliOutput.Configuration(new CliConfiguration().List(), settings.OutputFormat, output.Writer);
            output.Commit();
        });
}
internal sealed class ConfigGetCommand : Command<ConfigGetSettings>
{
    public override int Execute(CommandContext context, ConfigGetSettings settings, CancellationToken cancellationToken)
        => CliOutput.Run(() =>
        {
            using var output = new OutputDestination(settings.Output);
            CliOutput.Configuration([new CliConfiguration().Get(settings.Key)], settings.OutputFormat, output.Writer);
            output.Commit();
        });
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
