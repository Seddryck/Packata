using Packata.Core.Contracts;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class ValidateCommand : AsyncCommand<ContractSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ContractSettings settings, CancellationToken cancellationToken)
    {
        IReadOnlyList<MappingDiagnostic> diagnostics;
        int exitCode;
        try
        {
            var document = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken).ConfigureAwait(false);
            diagnostics = ContractValidation.Validate(document);
            exitCode = diagnostics.All(item => item.Severity != MappingSeverity.Error)
                ? ExitCodes.Success : ExitCodes.InvalidDocument;
        }
        catch (Exception exception)
        {
            diagnostics = [CommandOutput.Failure(exception)];
            exitCode = CommandOutput.FailureExitCode(exception);
        }

        try
        {
            Write(settings, exitCode == ExitCodes.Success, diagnostics);
            return exitCode;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CommandOutput.Diagnostic(CommandOutput.Failure(exception));
            return ExitCodes.Io;
        }
    }

    private static void Write(ContractSettings settings, bool valid, IReadOnlyList<MappingDiagnostic> diagnostics)
    {
        using var output = new OutputDestination(settings.Output);
        if (settings.OutputFormat == "json")
            CommandOutput.Json(CommandOutput.RedactedJson(new { schemaVersion = 1, source = settings.Source, valid, diagnostics }), output.Writer);
        else
        {
            output.Writer.WriteLine(valid ? "Contract is valid." : "Contract is invalid.");
            foreach (var diagnostic in diagnostics) CommandOutput.Diagnostic(diagnostic);
        }
        output.Commit();
    }
}
