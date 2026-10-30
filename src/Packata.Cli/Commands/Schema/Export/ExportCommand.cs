using Packata.Application;
using Packata.Core.Contracts;
using Spectre.Console.Cli;
using Packata.Application.Generation;

namespace Packata.Cli;

internal sealed class ExportCommand : AsyncCommand<ExportSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ExportSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken).ConfigureAwait(false);
            if (settings.GeneratesCode) return await GenerateAsync(source, settings, cancellationToken).ConfigureAwait(false);
            var result = ContractExportService.JsonSchema(source.ParsedDocument, settings.Asset ?? throw new ArgumentException("An asset is required."), cancellationToken);
            foreach (var diagnostic in result.Diagnostics) CommandOutput.Diagnostic(diagnostic);
            if (!result.IsSuccessful) return ExitCodes.InvalidDocument;
            var content = result.Content + Environment.NewLine;
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

    private static async Task<int> GenerateAsync(ContractSource source, ExportSettings settings, CancellationToken token)
    {
        TemplateInput? template = null;
        if (settings.Template is not null)
        {
            var uri = Uri.TryCreate(settings.Template, UriKind.Absolute, out var absolute) ? absolute : new Uri(source.BaseUri, settings.Template);
            if (!uri.IsFile) throw new NotSupportedException("Custom templates must be local files.");
            template = new(uri.AbsoluteUri, await File.ReadAllTextAsync(uri.LocalPath, token).ConfigureAwait(false));
        }
        var result = await new SchemaGenerationService().GenerateAsync(source.ParsedDocument, settings.GenerationOptions, template, token).ConfigureAwait(false);
        foreach (var diagnostic in result.Diagnostics)
            CommandOutput.Diagnostic(new(diagnostic.Code, Enum.Parse<MappingSeverity>(diagnostic.Severity),
                diagnostic.Template ?? diagnostic.SourcePath, diagnostic.Message));
        if (!result.IsSuccessful) return result.Diagnostics.Any(d => d.Code == "GEN_OPTIONS") ? ExitCodes.Invocation : ExitCodes.InvalidDocument;
        token.ThrowIfCancellationRequested();
        if (settings.Output is null) Console.Write(result.Content);
        else ContractDocumentWriter.Write(settings.Output, result.Content!, overwrite: false);
        return ExitCodes.Success;
    }
}
