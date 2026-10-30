using Newtonsoft.Json.Linq;
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

    public static void Diagnostic(MappingDiagnostic diagnostic) =>
        Console.Error.WriteLine($"{diagnostic.Code} {diagnostic.Severity} {Redact(diagnostic.SourcePath)}: {Redact(diagnostic.Message)}");

    public static string Redact(string value) => Packata.Application.ContractJson.Redact(value);

    public static JToken RedactedJson(object value) => Packata.Application.ContractJson.RedactedJson(value);
}
