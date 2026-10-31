using Packata.Application;
using Packata.Application.Generation;
using Packata.Core.Contracts;
using Spectre.Console.Cli;
namespace Packata.Cli;
internal sealed class GenerateCommand : AsyncCommand<GenerateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, GenerateSettings settings, CancellationToken token)
    {
        try
        {
            if (context.Remaining.Raw.Count > 0)
            {
                CommandOutput.Diagnostic(new("GEN_OPTIONS", MappingSeverity.Error, "$", "Unsupported generation option. Use schema generate --help."));
                return ExitCodes.Invocation;
            }
            using var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, token).ConfigureAwait(false);
            return await GenerateAsync(source, settings, token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            CommandOutput.Diagnostic(CommandOutput.Failure(exception));
            return CommandOutput.FailureExitCode(exception);
        }
    }

    private static async Task<int> GenerateAsync(ContractSource source, GenerateSettings settings, CancellationToken token)
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

