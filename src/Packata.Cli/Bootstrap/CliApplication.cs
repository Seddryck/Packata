using Spectre.Console;
using Spectre.Console.Cli;

namespace Packata.Cli;

internal static class CliApplication
{
    internal static int Run(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 1 && args[0] is "contract" or "data" or "schema" or "config" or "connector" or "dialect") args = [args[0], "--help"];
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
            configuration.SetExceptionHandler((exception, _) => CliErrorHandler.Invocation(exception,
                detail => CompareInvocationFailure.Write(args, detail)));
            ContractCommands.Configure(configuration);
            DialectCommands.Configure(configuration);
            DataCommands.Configure(configuration);
            configuration.AddCommand<ExportCommand>("export")
                .WithDescription("Export an asset as JSON Schema; compatibility alias for schema export.")
                .WithExample(["export", "sales.odcs.yaml", "--output-format", "json-schema", "--asset", "orders"]);
            configuration.AddCommand<InferCommand>("infer")
                .WithDescription("Infer a bound CSV, Parquet, or database schema; compatibility alias for schema infer.")
                .WithExample(["infer", "datapackage.json", "--asset", "customers"]);
            SchemaCommands.Configure(configuration);
            ConfigCommands.Configure(configuration);
            ConnectorCommandRegistration.Configure(configuration);
        });

        return app.Run(args, cancellationToken);
    }
}
