using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

[TestFixture]
public sealed class LineageExportCommandTests
{
    private const string OrdersContract = """
        apiVersion: v3.2.0
        kind: DataContract
        id: orders
        version: 1.0.0
        status: active
        schema:
          - name: order_lines
            properties:
              - name: quantity
                logicalType: integer
              - name: unit_price
                logicalType: number
          - name: prices
            properties:
              - name: total_price
                logicalType: number
                transformSourceObjects: [order_lines]
                transformLogic: quantity * unit_price
                transformDescription: Multiply quantity by unit price.
        """;

    [Test]
    public async Task Export_writes_the_declared_job_and_two_column_edges_to_stdout()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("orders.odcs.yaml", OrdersContract);
        var result = await CliWorkspace.Run(Arguments(source));
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Error, Is.Empty);
        var json = JObject.Parse(result.Output);
        Assert.That((string?)json["job"]?["name"], Is.EqualTo("build_prices"));
        Assert.That((string?)json["inputs"]?[0]?["name"], Is.EqualTo("commerce.raw.order_lines"));
        Assert.That((string?)json["outputs"]?[0]?["name"], Is.EqualTo("commerce.analytics.prices"));
        var fields = (JArray)json["outputs"]![0]!["facets"]!["columnLineage"]!["fields"]!["total_price"]!["inputFields"]!;
        Assert.That(fields.Select(field => (string?)field["field"]), Is.EqualTo(new[] { "quantity", "unit_price" }));
    }

    [Test]
    public async Task Export_creates_a_file_and_protects_existing_output()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("orders.odcs.yaml", OrdersContract);
        var output = System.IO.Path.Combine(workspace.Path, "lineage.json");
        var args = Arguments(source, "--output", output);
        var created = await CliWorkspace.Run(args);
        Assert.That(created.ExitCode, Is.Zero, created.Error);
        Assert.That(created.Output, Is.Empty);
        Assert.That(JObject.Parse(File.ReadAllText(output))["job"], Is.Not.Null);

        File.WriteAllText(output, "keep");
        var refused = await CliWorkspace.Run(args);
        Assert.That(refused.ExitCode, Is.EqualTo(3));
        Assert.That(File.ReadAllText(output), Is.EqualTo("keep"));
    }

    [Test]
    public async Task Export_reports_unresolved_source_identity_without_writing_json()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("orders.odcs.yaml", OrdersContract.Replace("transformSourceObjects: [order_lines]", "transformSourceObjects: [external_lines]"));
        var result = await CliWorkspace.Run(Arguments(source));
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain("ODCS009").And.Contain("OL003"));
    }

    [Test]
    public async Task Export_rejects_duplicate_source_mappings()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("orders.odcs.yaml", OrdersContract);
        var result = await CliWorkspace.Run(Arguments(source, "--source-dataset", "order_lines=commerce.raw.other_lines"));
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain("--source-dataset"));
    }

    [Test]
    public async Task Export_help_shows_the_required_identity_options()
    {
        var help = await CliWorkspace.Run("lineage", "export", "--help");
        Assert.That(help.ExitCode, Is.Zero);
        Assert.That(help.Output, Does.Contain("--asset").And.Contain("--job-name").And.Contain("--source-dataset").And.Contain("--output"));
    }

    private static string[] Arguments(string source, params string[] extra) =>
    [
        "lineage", "export", source, "--asset", "prices",
        "--job-namespace", "commerce-prod", "--job-name", "build_prices",
        "--dataset-namespace", "postgres://warehouse.example.com:5432",
        "--output-dataset", "commerce.analytics.prices",
        "--source-dataset", "order_lines=commerce.raw.order_lines",
        ..extra,
    ];
}
