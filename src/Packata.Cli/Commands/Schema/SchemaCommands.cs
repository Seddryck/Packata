using Spectre.Console.Cli;

namespace Packata.Cli;

internal static class SchemaCommands
{
    public static void Configure(IConfigurator configuration)
    {
        configuration.AddBranch("schema", schema =>
        {
            schema.SetDescription("Infer and export resource structure from contract assets.");
            schema.AddCommand<GenerateCommand>("generate")
                .WithDescription("Generate schema code using a target/language pair or a custom Didot template.");
            schema.AddCommand<ExportCommand>("export")
                .WithDescription("Export a portable schema document.")
                .WithExample(["schema", "export", "sales.odcs.yaml", "--output-format", "json-schema", "--asset", "orders"]);
            schema.AddCommand<InferCommand>("infer")
                .WithDescription("Infer the schema of a bound CSV, Excel, Parquet, or database asset.")
                .WithExample(["schema", "infer", "datapackage.json", "--asset", "customers"]);
        });
    }
}
