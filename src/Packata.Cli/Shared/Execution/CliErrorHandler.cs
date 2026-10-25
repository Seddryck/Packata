using Spectre.Console.Cli;

namespace Packata.Cli;

internal static class CliErrorHandler
{
    public static int Invocation(Exception exception, string[]? args = null)
    {
        if (exception is OperationCanceledException) return ExitCodes.Cancelled;
        // Conversion failures may embed arbitrary input, including credentials. Report option
        // guidance without echoing tokens; settings validation supplies safe contextual errors.
        var conversion = FindConversionError(exception);
        var detail = conversion?.Message ?? (exception is CommandRuntimeException && exception.InnerException is null
            ? CommandOutput.Redact(exception.Message)
            : "Invalid invocation. Check required arguments and option values; run packata --help for usage.");
        Console.Error.WriteLine($"CLI002 Error: {detail}");
        if (args is not null) WriteCompareFailure(args, detail);
        return ExitCodes.Invocation;
    }

    private static void WriteCompareFailure(string[] args, string detail)
    {
        if (args.Length < 2 || args[0] != "contract" || args[1] != "compare" || args.Contains("--quiet")) return;
        // Invocation errors cannot commit a destination before settings validation succeeds.
        if (args.Any(arg => arg is "-o" or "--output" || arg.StartsWith("--output=", StringComparison.Ordinal))) return;
        if (!RequestsJson(args)) return;
        CommandOutput.Json(ComparePresenter.Result(null, null, null,
            [new("invocation", "", "CLI002", Core.Contracts.MappingSeverity.Error, "$", detail)], null, ExitCodes.Invocation));
    }

    private static bool RequestsJson(string[] args)
    {
        var index = Array.IndexOf(args, "--output-format");
        return index >= 0 && index + 1 < args.Length && args[index + 1] == "json" || args.Contains("--output-format=json");
    }

    private static OptionConversionException? FindConversionError(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is OptionConversionException conversion) return conversion;
        return null;
    }
}
