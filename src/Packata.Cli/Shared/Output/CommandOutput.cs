using Newtonsoft.Json.Linq;
using Spectre.Console;
using Packata.Core.Contracts;

namespace Packata.Cli;

internal static class CommandOutput
{
    public static void Json(object value, TextWriter? writer = null) => (writer ?? Console.Out).WriteLine(Packata.Application.ContractJson.Serialize(value));

    // Exception messages from parsers and database drivers may contain source lines or credentials.
    public static MappingDiagnostic Failure(Exception exception) => exception switch
    {
        ContractOperationException operation => operation.Diagnostic,
        Packata.Application.ContractAssetSelectionException selection =>
            new(selection.Ambiguous ? "CLI011" : "CLI010", MappingSeverity.Error, "$.assets", selection.Message),
        OperationCanceledException => new("CLI130", MappingSeverity.Error, "$", "Operation cancelled."),
        IOException or UnauthorizedAccessException or HttpRequestException =>
            new("CLI003", MappingSeverity.Error, "$", "Unable to read the source or write output. Check locations and access permissions."),
        _ => new("CLI001", MappingSeverity.Error, "$", "Invalid or unsupported contract document. Check its syntax and declared format.")
    };

    public static int FailureExitCode(Exception exception) => exception switch
    {
        OperationCanceledException => ExitCodes.Cancelled,
        IOException or UnauthorizedAccessException or HttpRequestException => ExitCodes.Io,
        _ => ExitCodes.InvalidDocument
    };

    public static void Diagnostic(MappingDiagnostic diagnostic)
    {
        if (Console.IsErrorRedirected)
        {
            Console.Error.WriteLine(DiagnosticText(diagnostic));
            return;
        }
        Diagnostic(diagnostic, AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error),
            Ansi = AnsiSupport.Detect,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = true }
        }));
    }

    internal static void Diagnostic(MappingDiagnostic diagnostic, IAnsiConsole console)
    {
        var color = SeverityColor(diagnostic.Severity.ToString());
        var text = DiagnosticText(diagnostic);
        console.MarkupLine($"[{color}]{Markup.Escape(text)}[/]");
    }

    internal static string SeverityColor(string severity) => severity.ToLowerInvariant() switch
    {
        "warning" => "yellow", "information" or "info" => "blue", "error" => "red", _ => "grey"
    };

    private static string DiagnosticText(MappingDiagnostic diagnostic)
        => $"{diagnostic.Code} {diagnostic.Severity} {Redact(diagnostic.SourcePath)}: {Redact(diagnostic.Message)}";

    public static string Redact(string value) => Packata.Application.ContractJson.Redact(value);

    public static JToken RedactedJson(object value) => Packata.Application.ContractJson.RedactedJson(value);
}
