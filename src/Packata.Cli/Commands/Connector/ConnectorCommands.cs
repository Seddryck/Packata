using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class ConnectorListCommand : Command<ConnectorListSettings>
{
    public override int Execute(CommandContext context, ConnectorListSettings settings, CancellationToken cancellationToken)
        => CliOutput.Run(() =>
        {
            using var connectors = new CliConnectors(new CliConfiguration());
            using var output = new OutputDestination(settings.Output);
            CliConnectors.Write(connectors.List(settings.Scheme), settings.OutputFormat, settings.Verbose, output.Writer);
            output.Commit();
        });
}
internal sealed class ConnectorShowCommand : Command<ConnectorShowSettings>
{
    public override int Execute(CommandContext context, ConnectorShowSettings settings, CancellationToken cancellationToken)
        => CliOutput.Run(() =>
        {
            using var connectors = new CliConnectors(new CliConfiguration());
            using var output = new OutputDestination(settings.Output);
            CliConnectors.Write(connectors.Show(settings.Id), settings.OutputFormat, verbose: true, writer: output.Writer);
            output.Commit();
        });
}
internal sealed class ConnectorInstallCommand : AsyncCommand<ConnectorInstallSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectorInstallSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var connectors = new CliConnectors(new CliConfiguration());
            CliConnectors.Write([await connectors.InstallAsync(settings.Id, cancellationToken).ConfigureAwait(false)], "text", verbose: true);
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Connector installation was cancelled."); return 2; }
        catch (Exception error) { return CliOutput.Error(error); }
    }
}
