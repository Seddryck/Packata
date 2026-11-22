using Packata.Core.Contracts;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class PullCommand : AsyncCommand<PullSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PullSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient();
            var count = await DataPackagePull.RunAsync(client, new Uri(settings.Source), settings.OutputDirectory, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Downloaded {count} file{(count == 1 ? string.Empty : "s")}.");
            return ExitCodes.Success;
        }
        catch (DataPackagePull.TransferException transfer)
        {
            CommandOutput.Diagnostic(new MappingDiagnostic("CLI052", MappingSeverity.Error, transfer.Location, transfer.Message));
            return CommandOutput.FailureExitCode(transfer.InnerException!);
        }
        catch (Exception exception)
        {
            CommandOutput.Diagnostic(CommandOutput.Failure(exception));
            return CommandOutput.FailureExitCode(exception);
        }
    }
}
