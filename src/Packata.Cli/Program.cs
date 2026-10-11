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
            configuration.SetApplicationName("packata");
            configuration.SetApplicationVersion(ApplicationVersion.Value);
            configuration.AddCommand<InferCommand>("infer")
                .WithDescription("Infer the schema and CSV dialect of a bound contract asset.")
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
