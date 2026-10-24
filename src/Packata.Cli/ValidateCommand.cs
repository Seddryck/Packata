using System.ComponentModel;
using Packata.Core.Contracts;
using Packata.DataPackage.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal class ContractSettings : CommandSettings
{
    [CommandArgument(0, "<SOURCE>")]
    [Description("Local contract file, HTTP(S) descriptor, or ZIP package.")]
    public required string Source { get; init; }

    [CommandOption("--format <FORMAT>")]
    [Description("Contract standard: auto, odcs, or datapackage.")]
    [DefaultValue("auto")]
    public string Format { get; init; } = "auto";

    [CommandOption("--output <FORMAT>")]
    [Description("Output representation: text or json.")]
    [DefaultValue("text")]
    public string Output { get; init; } = "text";

    public override ValidationResult Validate() =>
        Format is not ("auto" or "odcs" or "datapackage")
            ? ValidationResult.Error("--format must be auto, odcs, or datapackage.")
            : Output is not ("text" or "json")
                ? ValidationResult.Error("--output must be text or json.")
                : ValidationResult.Success();
}

internal sealed class ValidateCommand : AsyncCommand<ContractSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ContractSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var document = await ContractSource.LoadAsync(settings.Source, settings.Format, cancellationToken).ConfigureAwait(false);
            var diagnostics = Validate(document);
            var valid = diagnostics.All(item => item.Severity != MappingSeverity.Error);
            Write(settings, valid, diagnostics);
            return valid ? ExitCodes.Success : ExitCodes.InvalidDocument;
        }
        catch (Exception exception)
        {
            Write(settings, false, [CommandOutput.Failure(exception)]);
            return CommandOutput.FailureExitCode(exception);
        }
    }

    internal static IReadOnlyList<MappingDiagnostic> Validate(ContractSource document)
    {
        if (document.NativeDocument is Packata.DataPackage.DataPackage package)
            return DataPackageValidator.Validate(package).Issues.Select(issue =>
                new MappingDiagnostic("DPV001", MappingSeverity.Error, issue.Path, issue.Message)).ToArray();

        var odcs = (Packata.OpenDataContract.DataContract)document.NativeDocument;
        var diagnostics = new List<MappingDiagnostic>();
        foreach (var (path, value) in new[] { ("id", odcs.Id), ("version", odcs.Version), ("status", odcs.Status) })
            if (string.IsNullOrWhiteSpace(value))
                diagnostics.Add(new("ODCSV001", MappingSeverity.Error, "$." + path, "A non-empty value is required."));
        if (odcs.Kind != "DataContract")
            diagnostics.Add(new("ODCSV002", MappingSeverity.Error, "$.kind", "kind must be DataContract."));
        if (odcs.ApiVersion != "v3.2.0")
            diagnostics.Add(new("ODCSV003", MappingSeverity.Error, "$.apiVersion", "Supported ODCS apiVersion is v3.2.0."));
        return diagnostics;
    }

    private static void Write(ContractSettings settings, bool valid, IReadOnlyList<MappingDiagnostic> diagnostics)
    {
        if (settings.Output == "json")
            CommandOutput.Json(CommandOutput.RedactedJson(new { schemaVersion = 1, source = settings.Source, valid, diagnostics }));
        else
        {
            Console.WriteLine(valid ? "Contract is valid." : "Contract is invalid.");
            foreach (var diagnostic in diagnostics) CommandOutput.Diagnostic(diagnostic);
        }
    }
}
