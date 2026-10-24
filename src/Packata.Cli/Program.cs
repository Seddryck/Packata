using System.Reflection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try { return Run(args, cancellation.Token); }
        finally { Console.CancelKeyPress -= cancel; }
    }

    internal static int Run(string[] args, CancellationToken cancellationToken)
    {
        var app = new CommandApp<RootCommand>();
        app.Configure(configuration =>
        {
            configuration.SetApplicationName("packata");
            configuration.SetApplicationVersion(ApplicationVersion.Value);
            configuration.ConfigureConsole(AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = Console.IsOutputRedirected ? AnsiSupport.No : AnsiSupport.Detect,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = !Console.IsOutputRedirected }
            }));
            configuration.CancellationExitCode(ExitCodes.Cancelled);
            configuration.SetExceptionHandler((exception, _) =>
            {
                if (exception is OperationCanceledException) return ExitCodes.Cancelled;
                var detail = exception is CommandRuntimeException
                    ? CommandOutput.Redact(exception.Message)
                    : args.Take(2).SequenceEqual(new[] { "data", "preview" })
                        ? "Invalid preview invocation. Supply <SOURCE> and --asset <ID>; --limit must be a positive 32-bit integer. Run packata data preview --help for usage."
                    : "Invalid invocation. Check required arguments and option values; run packata --help for usage.";
                var limitIndex = Array.IndexOf(args, "--limit");
                if (args.Take(2).SequenceEqual(new[] { "data", "preview" }) && limitIndex >= 0 &&
                    (limitIndex + 1 >= args.Length || !int.TryParse(args[limitIndex + 1], out var limit) || limit <= 0))
                    detail = "--limit must be a positive 32-bit integer.";
                Console.Error.WriteLine($"CLI002 Error: {detail}");
                return ExitCodes.Invocation;
            });
            configuration.AddBranch("contract", contract =>
            {
                contract.SetDescription("Validate and inspect contract documents.");
                contract.AddCommand<ValidateCommand>("validate")
                    .WithDescription("Validate an ODCS or Data Package v2 contract.")
                    .WithExample(["contract", "validate", "contract.yaml", "--output", "json"]);
                contract.AddCommand<InspectCommand>("inspect")
                    .WithDescription("Inspect canonical contract metadata, assets, and endpoints.")
                    .WithExample(["contract", "inspect", "contract.yaml", "--asset", "customers"]);
            });
            configuration.AddBranch("data", data =>
            {
                data.SetDescription("Read data bound to contract assets.");
                data.AddCommand<PreviewCommand>("preview")
                    .WithDescription("Preview a bounded sample of a bound contract asset.")
                    .WithExample(["data", "preview", "contract.yaml", "--asset", "customers", "--limit", "20"]);
            });
            configuration.AddCommand<InferCommand>("infer")
                .WithDescription("Infer the schema of a bound CSV or database contract asset.")
                .WithExample(["datapackage.json", "--asset", "customers"]);
        });

        return app.Run(args, cancellationToken);
    }
}

internal static class ApplicationVersion
{
    public static string Value { get; } = Resolve();

    private static string Resolve()
    {
        var informationalVersion = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return informationalVersion?.Split('+', 2)[0] ?? "0.0.0";
    }
}
