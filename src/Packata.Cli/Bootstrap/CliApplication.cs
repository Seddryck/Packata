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
        if (args.FirstOrDefault() == "export" || (args.FirstOrDefault() == "schema" && args.Any(a => new[] { "--record", "--datatable", "--polars", "--pyspark", "--arrow", "--dataframe", "--idataview", "--ddl", "--json-schema", "--spark-json", "--avro", "--format", "--dataset" }.Contains(a))))
        {
            Console.Error.WriteLine("Removed schema syntax. Use schema export --output-format <format> or schema generate --target <target> --language <language>; use --asset for selection.");
            return ExitCodes.Invocation;
        }
        if (args.Take(2).SequenceEqual(new[] { "schema", "generate" }) && args.Any(a => a is "--output-format" or "-f" or "--profile")
            || args.Take(2).SequenceEqual(new[] { "schema", "export" }) && args.Any(a => a is "--target" or "--language" or "--dialect" or "--template" or "--engine" or "--type-mapping" or "--namespace" or "--type-name" or "--define" or "--mapping-fallback" or "--constraints" or "--no-descriptions"))
        {
            Console.Error.WriteLine("Options belong to another schema operation. Use schema export --output-format for documents or schema generate --target --language for code.");
            return ExitCodes.Invocation;
        }
        if (args.Length == 1 && args[0] is "contract" or "data" or "schema" or "config" or "connector" or "dialect" or "quality") args = [args[0], "--help"];
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
            configuration.CancellationExitCode(args.FirstOrDefault() == "quality" || args.Take(2).SequenceEqual(new[] { "contract", "satisfies" }) ? 2 : ExitCodes.Cancelled);
            configuration.SetExceptionHandler((exception, _) => HandleInvocation(args, exception));
            ContractCommands.Configure(configuration);
            DialectCommands.Configure(configuration);
            DataCommands.Configure(configuration);
            QualityCommands.Configure(configuration);
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
