using Packata.Core.Contracts;
using Packata.Documents.Validation;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal sealed class ValidateCommand : AsyncCommand<ValidateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ValidateSettings settings, CancellationToken cancellationToken)
    {
        IReadOnlyList<ValidationFinding> diagnostics;
        IReadOnlyList<LayerValidation> layers;
        var selected = ValidationLayers.None;
        if (settings.Schema) selected |= ValidationLayers.Schema;
        if (settings.Semantic) selected |= ValidationLayers.Semantic;
        if (selected == ValidationLayers.None) selected = DescriptorValidation.DefaultLayers;
        int exitCode;
        try
        {
            using var document = await ContractSource.LoadAsync(settings.Source, settings.InputFormat, cancellationToken, rawOnly: true).ConfigureAwait(false);
            var report = DescriptorValidation.Validate(document.OriginalText, settings.InputFormat, selected, document.BaseUri);
            diagnostics = report.Diagnostics;
            layers = report.Layers;
            exitCode = report.Valid ? ExitCodes.Success : ExitCodes.InvalidDocument;
        }
        catch (Exception exception)
        {
            var failure = CommandOutput.Failure(exception);
            diagnostics = [new(ValidationLayers.None, failure.Code, failure.Severity, failure.SourcePath, failure.Message)];
            layers = new[] { ValidationLayers.Schema, ValidationLayers.Semantic }.Where(layer => selected.HasFlag(layer))
                .Select(layer => new LayerValidation(layer, ValidationState.Skipped, "Source loading prerequisite failed.")).ToArray();
            exitCode = CommandOutput.FailureExitCode(exception);
        }

        try
        {
            Write(settings, exitCode == ExitCodes.Success, diagnostics, layers);
            return exitCode;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CommandOutput.Diagnostic(CommandOutput.Failure(exception));
            return ExitCodes.Io;
        }
    }

    private static void Write(ContractSettings settings, bool valid, IReadOnlyList<ValidationFinding> diagnostics, IReadOnlyList<LayerValidation> layers)
    {
        using var output = new OutputDestination(settings.Output);
        if (settings.OutputFormat == "json")
            CommandOutput.Json(CommandOutput.RedactedJson(new { schemaVersion = 1, source = settings.Source, valid, layers, diagnostics }), output.Writer);
        else
        {
            output.Writer.WriteLine(valid ? "Contract is valid." : "Contract is invalid.");
            foreach (var layer in layers) output.Writer.WriteLine($"{layer.Layer}: {layer.State}{(layer.Reason is null ? "" : " — " + layer.Reason)}");
            foreach (var diagnostic in diagnostics) CommandOutput.Diagnostic(new(diagnostic.Code, diagnostic.Severity, diagnostic.SourcePath, $"[{diagnostic.Layer}] {diagnostic.Message}"));
        }
        output.Commit();
    }
}
