using Spectre.Console.Cli;
namespace Packata.Cli;
internal sealed class GenerateCommand : AsyncCommand<GenerateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, GenerateSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken);
            return await ExportCommand.GenerateAsync(source, settings.GenerationOptions, settings.Template, settings.Output, cancellationToken);
        }
        catch (Exception error) { CommandOutput.Diagnostic(CommandOutput.Failure(error)); return CommandOutput.FailureExitCode(error); }
    }
}
