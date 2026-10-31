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
            using var source = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken);
            return await GenerateAsync(source, settings.Options, null, settings.Output, cancellationToken);
        }
        catch (Exception error) { CommandOutput.Diagnostic(CommandOutput.Failure(error)); return CommandOutput.FailureExitCode(error); }
    }
    internal static async Task<int> GenerateAsync(ContractSource source, GenerationOptions options, string? templatePath, string? output, CancellationToken token)
    {
        var template = await ReadTemplateAsync(source, templatePath, token);
        var result = await new SchemaGenerationService().GenerateAsync(source.ParsedDocument, options, template, token).ConfigureAwait(false);
        foreach (var diagnostic in result.Diagnostics)
            CommandOutput.Diagnostic(new(diagnostic.Code, Enum.Parse<MappingSeverity>(diagnostic.Severity),
                diagnostic.Template ?? diagnostic.SourcePath, diagnostic.Message));
        if (!result.IsSuccessful) return result.Diagnostics.Any(d => d.Code == "GEN_OPTIONS") ? ExitCodes.Invocation : ExitCodes.InvalidDocument;
        token.ThrowIfCancellationRequested();
        Publish(result, output);
        return ExitCodes.Success;
    }
    private static async Task<TemplateInput?> ReadTemplateAsync(ContractSource source, string? templatePath, CancellationToken token)
    {
        if (templatePath is null) return null;
        var uri = Uri.TryCreate(templatePath, UriKind.Absolute, out var absolute) ? absolute : new Uri(source.BaseUri, templatePath);
        if (!uri.IsFile) throw new NotSupportedException("Custom templates must be local files.");
        return new(uri.AbsoluteUri, await File.ReadAllTextAsync(uri.LocalPath, token).ConfigureAwait(false));
    }

    private static void Publish(GenerationResult result, string? output)
    {
        if (result.Representation == "binary")
        {
            if (output is null) throw new ArgumentException("Binary schema artifacts require --output <path>.");
            ContractDocumentWriter.Write(output, result.BinaryContent!, overwrite: false);
        }
        else if (output is null) Console.Write(result.Content);
        else ContractDocumentWriter.Write(output, result.Content!, overwrite: false);
    }
}
