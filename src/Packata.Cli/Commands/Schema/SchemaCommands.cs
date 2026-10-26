using Spectre.Console.Cli;

namespace Packata.Cli;

internal static class SchemaCommands
{
    public static void Configure(IConfigurator configuration)
    {
        configuration.AddBranch("schema", schema =>
        {
            schema.SetDescription("Infer and export resource structure from contract assets.");
            schema.AddCommand<ExportCommand>("export")
                .WithDescription("Export an asset as JSON Schema Draft 2020-12.")
                .WithExample(["schema", "export", "sales.odcs.yaml", "--output-format", "json-schema", "--asset", "orders"]);
            schema.AddCommand<InferCommand>("infer")
                .WithDescription("Infer the schema of a bound CSV, Parquet, or database asset.")
                .WithExample(["schema", "infer", "datapackage.json", "--asset", "customers"]);
        });
    }
}
