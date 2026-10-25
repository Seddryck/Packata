using System.ComponentModel;
using System.Text;
using Packata.Core.Contracts;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class ExportSettings : CommandSettings
{
    [CommandArgument(0, "<SOURCE>")]
    [Description("Local contract file, HTTP(S) descriptor, or ZIP package.")]
    public required string Source { get; init; }

    [CommandOption("-f|--format <FORMAT>")]
    [Description("Target format: json-schema (required).")]
    public string? Format { get; init; }

    [CommandOption("-d|--dataset <NAME>")]
    [Description("Exact canonical dataset identifier to export (required).")]
    public string? Dataset { get; init; }

    [CommandOption("-o|--output <PATH>")]
    [Description("Write JSON to a new file instead of stdout. Existing files are protected.")]
    public string? Output { get; init; }

    public override ValidationResult Validate() => Format != "json-schema"
        ? ValidationResult.Error("--format must be json-schema.")
        : string.IsNullOrWhiteSpace(Dataset)
            ? ValidationResult.Error("--dataset is required.")
            : ValidationResult.Success();
}

internal sealed class ExportCommand : AsyncCommand<ExportSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ExportSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var source = await ContractSource.LoadAsync(settings.Source, "auto", cancellationToken).ConfigureAwait(false);
            var mapped = ContractSelection.Map(source);
            foreach (var diagnostic in mapped.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!mapped.IsSuccessful) return ExitCodes.InvalidDocument;
            var schema = ContractSelection.Asset(mapped.Value!, settings.Dataset!).ToJsonSchema();
            using var stream = new MemoryStream();
            schema.Serialize(stream);
            var content = Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.Output is null) Console.Write(content);
            else
            {
                try { ContractDocumentWriter.Write(settings.Output, content, overwrite: false); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    CommandOutput.Diagnostic(new("CLI003", MappingSeverity.Error, "$.output",
                        "Unable to write output. Choose a new file and check directory permissions."));
                    return ExitCodes.Io;
                }
            }
            return ExitCodes.Success;
        }
        catch (JsonSchemaConversionException exception)
        {
            CommandOutput.Diagnostic(new("CLI020", MappingSeverity.Error, "$.schema", exception.Message));
            return ExitCodes.InvalidDocument;
        }
        catch (Exception exception)
        {
            CommandOutput.Diagnostic(CommandOutput.Failure(exception));
            return CommandOutput.FailureExitCode(exception);
        }
    }
}
