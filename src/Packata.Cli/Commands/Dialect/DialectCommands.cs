using Spectre.Console.Cli;

namespace Packata.Cli;

internal static class DialectCommands
{
    public static void Configure(IConfigurator configuration)
    {
        configuration.AddBranch("dialect", dialect =>
        {
            dialect.SetDescription("Export endpoint parsing configuration.");
            dialect.AddCommand<DialectExportCommand>("export")
                .WithDescription("Export a selected CSV endpoint as a standalone Data Package v2 Table Dialect JSON descriptor.")
                .WithExample(["dialect", "export", "datapackage.json", "--endpoint", "customers-endpoint"]);
        });
    }
}
