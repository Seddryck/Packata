using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

// Parsing or validation may fail before CompareSettings exists. Recover only the
// presentation choices through Spectre's parser; never execute a comparison or write a file.
internal static class CompareInvocationFailure
{
    internal static void Write(string[] args, string detail)
    {
        var app = new CommandApp();
        app.Configure(configuration =>
        {
            configuration.Settings.StrictParsing = false;
            configuration.ConfigureConsole(AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(TextWriter.Null)
            }));
            configuration.SetExceptionHandler((_, _) => ExitCodes.Invocation);
            configuration.AddBranch("contract", contract => contract.AddCommand<CaptureCommand>("compare")
                .WithData((Action<PresentationOptions>)(settings => Write(settings, detail))));
        });
        app.Run(args);
    }

    private static void Write(PresentationOptions settings, string detail)
    {
        if (settings.OutputFormat != "json" || settings.Quiet || settings.Output is not null) return;
        CommandOutput.Json(ComparePresenter.Result(null, null, null,
            [new("invocation", "", "CLI002", Core.Contracts.MappingSeverity.Error, "$", detail)], null, ExitCodes.Invocation));
    }

    private sealed class PresentationOptions : PresentationSettings
    {
        [CommandArgument(0, "[ARGUMENTS]")]
        public string[] Arguments { get; init; } = [];

        [CommandOption("--quiet")]
        public bool Quiet { get; init; }

        // Invalid business options and missing sources must not prevent reading
        // the presentation options. The real command already rejected the invocation.
        public override ValidationResult Validate() => ValidationResult.Success();
    }

    private sealed class CaptureCommand : Command<PresentationOptions>
    {
        public override int Execute(CommandContext context, PresentationOptions settings, CancellationToken cancellationToken)
        {
            if (context.Data is Action<PresentationOptions> report) report(settings);
            return ExitCodes.Success;
        }
    }
}
