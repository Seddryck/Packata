using System.Reflection;
using Spectre.Console.Cli;

namespace Packata.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var app = new CommandApp<RootCommand>();
        app.Configure(configuration =>
        {
            configuration.AddBranch("contract", contract =>
                contract.AddCommand<SatisfiesCommand>("satisfies")
                    .WithDescription("Evaluate consumer requirements against an ODCS or Data Package contract."));
            if (args.Length >= 2 && args[0] == "contract" && args[1] == "satisfies")
                configuration.SetExceptionHandler((exception, _) =>
                {
                    if (!args.Contains("--quiet")) Console.Error.WriteLine(exception.Message);
                    return 2;
                });
            configuration.SetApplicationName("packata");
            configuration.SetApplicationVersion(ApplicationVersion.Value);
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
