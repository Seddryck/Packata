using Spectre.Console.Cli;

namespace Packata.Cli;

internal static class ContractCommands
{
    private const string BranchName = "contract";

    public static void Configure(IConfigurator configuration)
    {
        configuration.AddBranch(BranchName, contract =>
        {
            contract.SetDescription("Validate, inspect, and compare contract documents.");
            contract.AddCommand<SatisfiesCommand>("satisfies")
                .WithDescription("Evaluate producer guarantees against a complete consumer contract.")
                .WithExample(BranchName, "satisfies", "producer.odcs.yaml", "--consumer", "consumer.odcs.yaml", "--format", "json");
            contract.AddCommand<CompareCommand>("compare")
                .WithDescription("Assess whether candidate data remains safe for consumers built against the baseline.")
                .WithExample(BranchName, "compare", "sales-v1.odcs.yaml", "sales-v2.odcs.yaml", "--output-format", "json");
            contract.AddCommand<DiffCommand>("diff")
                .WithDescription("Compare a baseline (previous) contract to a candidate (current) contract.")
                .WithExample(BranchName, "diff", "sales-v1.odcs.yaml", "sales-v2.odcs.yaml", "--output-format", "json");
            contract.AddCommand<ValidateCommand>("validate")
                .WithDescription("Validate an ODCS or Data Package v2 contract.")
                .WithExample(BranchName, "validate", "contract.yaml", "--output-format", "json");
            contract.AddCommand<InspectCommand>("inspect")
                .WithDescription("Inspect canonical contract metadata, assets, and endpoints.")
                .WithExample(BranchName, "inspect", "contract.yaml", "--asset", "customers");
        });
    }
}
