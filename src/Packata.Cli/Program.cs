using System.Reflection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

public static class Program
{
    public static int Main(string[] args)
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
            configuration.SetExceptionHandler((exception, _) =>
            {
                var detail = exception is CommandRuntimeException
                    ? CommandOutput.Redact(exception.Message)
                    : "Invalid invocation. Check required arguments and option values; run packata --help for usage.";
                Console.Error.WriteLine($"CLI002 Error: {detail}");
                return ExitCodes.Invocation;
            });
            configuration.AddBranch("contract", contract =>
            {
                contract.SetDescription("Validate and inspect contract documents.");
                contract.AddCommand<ValidateCommand>("validate")
                    .WithDescription("Validate an ODCS or Data Package v2 contract.")
                    .WithExample(["contract.yaml", "--output", "json"]);
                contract.AddCommand<InspectCommand>("inspect")
                    .WithDescription("Inspect canonical contract metadata, assets, and endpoints.")
                    .WithExample(["contract.yaml", "--asset", "customers"]);
            });
            configuration.AddCommand<InferCommand>("infer")
                .WithDescription("Infer the schema of a bound CSV or database contract asset.")
                .WithExample(["datapackage.json", "--asset", "customers"]);
        });

        return app.Run(args);
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
