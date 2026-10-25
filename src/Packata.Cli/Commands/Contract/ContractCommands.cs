using Spectre.Console.Cli;

namespace Packata.Cli;

internal static class ContractCommands
{
    public static void Configure(IConfigurator configuration)
    {
        configuration.AddBranch("contract", contract =>
        {
            contract.SetDescription("Validate, inspect, and compare contract documents.");
            contract.AddCommand<DiffCommand>("diff")
                .WithDescription("Compare a baseline (previous) contract to a candidate (current) contract.")
                .WithExample(["contract", "diff", "sales-v1.odcs.yaml", "sales-v2.odcs.yaml", "--output-format", "json"]);
            contract.AddCommand<ValidateCommand>("validate")
                .WithDescription("Validate an ODCS or Data Package v2 contract.")
                .WithExample(["contract", "validate", "contract.yaml", "--output-format", "json"]);
            contract.AddCommand<InspectCommand>("inspect")
                .WithDescription("Inspect canonical contract metadata, assets, and endpoints.")
                .WithExample(["contract", "inspect", "contract.yaml", "--asset", "customers"]);
        });
    }
}
