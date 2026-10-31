using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal static class CliApplication
{
    private static int HandleInvocation(string[] args, Exception exception)
    {
        if (args.FirstOrDefault() == "quality")
        {
            CliErrorHandler.Invocation(exception);
            return 2;
        }
        if (args.Take(2).SequenceEqual(new[] { "contract", "satisfies" }))
        {
            if (args.Contains("--quiet")) return 2;
            CliErrorHandler.Invocation(exception, detail => SatisfiesInvocationFailure.Write(args, detail));
            return 2;
        }
        return CliErrorHandler.Invocation(exception, detail => CompareInvocationFailure.Write(args, detail));
    }
    internal static int Run(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 1 && args[0] is "contract" or "data" or "schema" or "config" or "connector" or "dialect" or "quality") args = [args[0], "--help"];
        var app = new CommandApp<RootCommand>();
        app.Configure(configuration =>
        {
            if (args.Length >= 2 && args[0] == "schema" && args[1] is "generate" or "export") configuration.UseStrictParsing();
            configuration.SetApplicationName("packata");
            configuration.SetApplicationVersion(ApplicationVersion.Value);
            configuration.ConfigureConsole(AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = Console.IsOutputRedirected ? AnsiSupport.No : AnsiSupport.Detect,
                Enrichment = new ProfileEnrichment { UseDefaultEnrichers = !Console.IsOutputRedirected }
            }));
            configuration.CancellationExitCode(args.FirstOrDefault() == "quality" || args.Take(2).SequenceEqual(new[] { "contract", "satisfies" }) ? 2 : ExitCodes.Cancelled);
            configuration.SetExceptionHandler((exception, _) => HandleInvocation(args, exception));
            ContractCommands.Configure(configuration);
            DialectCommands.Configure(configuration);
            DataCommands.Configure(configuration);
            QualityCommands.Configure(configuration);
            configuration.AddCommand<ExportCommand>("export")
                .WithDescription("Export an asset as JSON Schema; compatibility alias for schema export.")
                .WithExample(["export", "sales.odcs.yaml", "--output-format", "json-schema", "--asset", "orders"]);
            configuration.AddCommand<InferCommand>("infer")
                .WithDescription("Infer a bound CSV, Excel, Parquet, or database schema; compatibility alias for schema infer.")
                .WithExample(["infer", "datapackage.json", "--asset", "customers"]);
            SchemaCommands.Configure(configuration);
            ConfigCommands.Configure(configuration);
            ConnectorCommandRegistration.Configure(configuration);
        });

        return app.Run(args, cancellationToken);
    }
}
