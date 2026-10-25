using Spectre.Console.Cli;

namespace Packata.Cli;

internal static class CliErrorHandler
{
    public static int Invocation(Exception exception)
    {
        if (exception is OperationCanceledException) return ExitCodes.Cancelled;
        // Conversion failures may embed arbitrary input, including credentials. Report option
        // guidance without echoing tokens; settings validation supplies safe contextual errors.
        var conversion = FindConversionError(exception);
        var detail = conversion?.Message ?? (exception is CommandRuntimeException && exception.InnerException is null
            ? CommandOutput.Redact(exception.Message)
            : "Invalid invocation. Check required arguments and option values; run packata --help for usage.");
        Console.Error.WriteLine($"CLI002 Error: {detail}");
        return ExitCodes.Invocation;
    }

    private static OptionConversionException? FindConversionError(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is OptionConversionException conversion) return conversion;
        return null;
    }
}
