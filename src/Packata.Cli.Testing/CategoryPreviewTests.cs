using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

public class CategoryPreviewTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Preview_flag_decodes_both_formats_and_keeps_source_files(bool odcs, bool decode)
    {
        using var workspace = new CliWorkspace();
        var descriptor = odcs ? Odcs : Package;
        var source = workspace.Write(odcs ? "contract.yaml" : "datapackage.json", descriptor);
        const string csv = "direction,rating\r\nS,1\r\nN,2\r\nX,3\r\n";
        var data = workspace.Write("directions.csv", csv);
        var args = new List<string> { "data", "preview", source, "--asset", "directions", "--output-format", "json" };
        if (decode) args.Add("--decode-categories");
        var result = await CliWorkspace.Run(args.ToArray());
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var rows = JObject.Parse(result.Output)["rows"]!;
        Assert.That((string?)rows[0]![0], Is.EqualTo(decode ? "South" : "S"));
        Assert.That(rows[0]![1]!.Type, Is.EqualTo(decode ? JTokenType.String : JTokenType.Integer));
        Assert.That(rows[0]![1]!.ToString(), Is.EqualTo(decode ? "Low" : "1"));
        Assert.That((string?)rows[1]![0], Is.EqualTo("N"));
        Assert.That(rows[1]![1]!.Type, Is.EqualTo(decode ? JTokenType.String : JTokenType.Integer));
        Assert.That(rows[1]![1]!.ToString(), Is.EqualTo("2"));
        Assert.That((string?)rows[2]![0], Is.EqualTo("X"));
        Assert.That(rows[2]![1]!.Type, Is.EqualTo(decode ? JTokenType.String : JTokenType.Integer));
        Assert.That(rows[2]![1]!.ToString(), Is.EqualTo("3"));
        Assert.That(File.ReadAllText(source), Is.EqualTo(descriptor));
        Assert.That(File.ReadAllText(data), Is.EqualTo(csv));
    }

    [TestCase("xlsx")]
    [TestCase("parquet")]
    [TestCase("inline")]
    public async Task Preview_reports_unsupported_engine_explicitly_before_resource_access(string format)
    {
        using var workspace = new CliWorkspace();
        var descriptor = JObject.Parse(Package);
        var resource = descriptor["resources"]![0]!;
        if (format == "inline") { ((JObject)resource).Remove("path"); resource["data"] = new JArray(); }
        else { resource["format"] = format; resource["path"] = "missing." + format; }
        var source = workspace.Write("datapackage.json", descriptor.ToString());
        var result = await CliWorkspace.Run("data", "preview", source, "--asset", "directions", "--decode-categories");
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain("--decode-categories").And.Contain("PocketCsvReader"));
    }

    [Test]
    public async Task Preview_help_exposes_decode_flag_and_scope()
    {
        var result = await CliWorkspace.Run("data", "preview", "--help");
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.Output, Does.Contain("--decode-categories").And.Contain("PocketCsvReader"));
    }

    private const string Package = """
        {"resources":[{"name":"directions","path":"directions.csv","format":"csv","dialect":{"header":true},"schema":{"fields":[
          {"name":"direction","type":"string","categories":[{"value":"S","label":"South"},{"value":"N"}]},
          {"name":"rating","type":"integer","categories":[{"value":1,"label":"Low"},{"value":2}]}
        ]}}]}
        """;
    private const string Odcs = """
        apiVersion: v3.2.0
        kind: DataContract
        id: directions
        version: 1.0.0
        status: draft
        schema:
          - name: directions
            physicalType: file
            properties:
              - name: direction
                logicalType: string
                enum:
                  - value: S
                    label: South
                  - value: N
              - name: rating
                logicalType: integer
                enum:
                  - value: 1
                    label: Low
                  - value: 2
        servers:
          - server: local
            type: local
            path: directions.csv
            format: csv
        """;
}
