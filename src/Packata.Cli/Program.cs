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
        if (args.Length == 1 && args[0] is "schema" or "config" or "connector") args = [args[0], "--help"];
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
                .WithDescription("Infer a bound CSV or database schema; compatibility alias for schema infer.")
                .WithExample(["infer", "datapackage.json", "--asset", "customers"]);
            configuration.AddBranch("schema", schema =>
            {
                schema.SetDescription("Infer resource structure from contract asset bindings.");
                schema.AddCommand<InferCommand>("infer")
                    .WithDescription("Infer the schema of a bound CSV or database asset.")
                    .WithExample(["schema", "infer", "datapackage.json", "--asset", "customers"]);
            });
            configuration.AddBranch("config", config =>
            {
                config.SetDescription("User settings: connectors.offline (false; downloads only), connectors.store (per-user; DUBURL_PROVIDER_STORE overrides), connectors.catalog (bundled), connectors.preferred.<scheme>. PACKATA_CONFIG_HOME overrides the user config directory.");
                config.AddCommand<ConfigListCommand>("list").WithDescription("Show effective settings and origins.");
                config.AddCommand<ConfigGetCommand>("get").WithDescription("Read an effective setting.");
                config.AddCommand<ConfigSetCommand>("set").WithDescription("Persist a user setting; filesystem paths become absolute.");
                config.AddCommand<ConfigUnsetCommand>("unset").WithDescription("Remove a user override, preserving installed connectors.");
            });
            configuration.AddBranch("connector", connector =>
            {
                connector.SetDescription("Discover and prepare curated database connectors using shared user configuration.");
                connector.AddCommand<ConnectorListCommand>("list").WithDescription("List catalog and installed connectors without downloading or loading them.");
                connector.AddCommand<ConnectorShowCommand>("show").WithDescription("Show versions, availability, compatibility and prerequisites.");
                connector.AddCommand<ConnectorInstallCommand>("install").WithDescription("Install and activate the pinned connector; respects connectors.offline.");
            });
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
