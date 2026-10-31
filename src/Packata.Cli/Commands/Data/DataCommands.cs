using Spectre.Console.Cli;

namespace Packata.Cli;

internal static class DataCommands
{
    public static void Configure(IConfigurator configuration)
    {
        configuration.AddBranch("data", data =>
        {
            data.SetDescription("Read data bound to contract assets.");
            data.AddCommand<DataValidateCommand>("validate")
                .WithDescription("Validate dataset structure against contract matching, order, types, and relationships.")
                .WithExample(["data", "validate", "contract.yaml", "--asset", "customers", "--output-format", "json"]);
            data.AddCommand<PreviewCommand>("preview")
                .WithDescription("Preview a bounded sample of a bound contract asset.")
                .WithExample(["data", "preview", "contract.yaml", "--asset", "customers", "--limit", "20"]);
        });
    }
}
