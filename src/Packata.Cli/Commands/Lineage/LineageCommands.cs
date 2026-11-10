using Spectre.Console.Cli;

namespace Packata.Cli;

internal static class LineageCommands
{
    public static void Configure(IConfigurator configuration)
    {
        configuration.AddBranch("lineage", lineage =>
        {
            lineage.SetDescription("Work with lineage declared in contracts.");
            lineage.AddCommand<LineageExportCommand>("export")
                .WithDescription("Export declared lineage for an asset as an OpenLineage JobEvent.")
                .WithExample(["lineage", "export", "orders.odcs.yaml", "--asset", "prices",
                    "--job-namespace", "commerce-prod", "--job-name", "build_prices",
                    "--dataset-namespace", "postgres://warehouse.example.com:5432",
                    "--output-dataset", "commerce.analytics.prices",
                    "--source-dataset", "order_lines=commerce.raw.order_lines"]);
        });
    }
}
