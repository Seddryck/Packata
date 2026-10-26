using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Testing;

namespace Packata.Cli.Testing;

public sealed class ExcelInferenceTests
{
    [TestCase("json", false)]
    [TestCase("json", true)]
    [TestCase("yaml", false)]
    [TestCase("yaml", true)]
    [TestCase("odcs", false)]
    [TestCase("odcs", true)]
    public async Task Infer_PersistsLayoutAndPreservesAuthoredConstraints(string standard, bool write)
    {
        using var workspace = new CliWorkspace();
        using (var workbook = ExcelWorkbookFixture.Create([[null, null], ["id", "name"], [1, "Alice"], [2, "Bob"]]))
            File.WriteAllBytes(Path.Combine(workspace.Path, "data.xlsx"), workbook.ToArray());
        var content = standard switch
        {
            "odcs" => CliWorkspace.Odcs.Replace("customers.csv", "data.xlsx").Replace("format: csv", "format: xlsx")
                .Replace("physicalType: file", "physicalType: file\n    properties:\n      - name: id\n        logicalType: integer\n        required: true\n        description: Keep this"),
            "yaml" => """
                name: sample
                title: Keep title
                resources:
                  - name: customers
                    path: data.xlsx
                    format: xlsx
                    schema:
                      fields:
                        - name: id
                          type: integer
                          description: Keep this
                          constraints:
                            required: true
                    dialect:
                      type: spreadsheet
                      sheetNumber: 1
                      header: true
                """,
            _ => """{"name":"sample","title":"Keep title","resources":[{"name":"customers","path":"data.xlsx","format":"xlsx","schema":{"fields":[{"name":"id","type":"integer","description":"Keep this","constraints":{"required":true}}]},"dialect":{"type":"spreadsheet","sheetNumber":1,"header":true}}]}"""
        };
        var source = workspace.Write(standard == "json" ? "package.json" : "contract.yaml", content);
        var target = write ? source : Path.Combine(workspace.Path, standard == "json" ? "output.json" : "output.yaml");
        var result = await CliWorkspace.Run(["schema", "infer", source, "--asset", "customers", "--header", "present",
            .. (write ? new[] { "--write" } : new[] { "--output", target })]);
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        var updated = ContractDocument.Load(target);
        var fields = updated.Contract.Assets.Single().Schema!.Fields;
        Assert.Multiple(() =>
        {
            Assert.That(fields.Select(field => field.Name), Is.EqualTo(new[] { "id", "name" }));
            Assert.That(fields[0].Required, Is.True);
            Assert.That(File.ReadAllText(target), Does.Contain("Keep this").And.Not.Contain("delimited"));
            Assert.That(updated.Contract.Endpoints.Single().Format!.Options["sheetName"], Is.EqualTo("Data"));
        });
        if (!write) Assert.That(File.ReadAllText(source), Is.EqualTo(content));
        var preview = await CliWorkspace.Run("data", "preview", target, "--asset", "customers");
        Assert.That(preview.ExitCode, Is.Zero, preview.Output + preview.Error);
        Assert.That(preview.Output, Does.Contain("Alice"));
        if (standard == "json") Assert.That((string?)JObject.Parse(File.ReadAllText(target))["title"], Is.EqualTo("Keep title"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Infer_DisplayAndAliasUseSheetOverrideWithoutWriting(bool alias)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", CliWorkspace.Odcs.Replace("customers.csv", "data.bin")
            .Replace("format: csv", "format: xlsx"));
        File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources", "my-book.xlsx"), Path.Combine(workspace.Path, "data.bin"));
        var before = File.ReadAllText(source);
        var result = await CliWorkspace.Run([.. (alias ? new[] { "infer" } : new[] { "schema", "infer" }), source,
            "--asset", "customers", "--sheet-name", "Country", "--header", "present"]);
        Assert.That(result.ExitCode, Is.Zero, result.Output + result.Error);
        Assert.That(result.Output, Does.Contain("Code").And.Contain("EXCEL001").And.Not.Contain("CSV dialect"));
        Assert.That(File.ReadAllText(source), Is.EqualTo(before));
    }

    [TestCase("--culture", "en-US")]
    [TestCase("--sheet-number", "0")]
    [TestCase("--sheet-name", "missing")]
    public async Task Infer_FailuresNeverModifySource(string option, string value)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", CliWorkspace.Odcs.Replace("customers.csv", "data.xlsx").Replace("format: csv", "format: xlsx"));
        File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources", "my-book.xlsx"), Path.Combine(workspace.Path, "data.xlsx"));
        var before = File.ReadAllText(source);
        var result = await CliWorkspace.Run("schema", "infer", source, "--asset", "customers", option, value, "--write");
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(File.ReadAllText(source), Is.EqualTo(before));
    }

    [Test]
    public async Task Infer_EmptyWorksheetCannotEraseAuthoredFields()
    {
        using var workspace = new CliWorkspace();
        using (var workbook = ExcelWorkbookFixture.Create([]))
            File.WriteAllBytes(Path.Combine(workspace.Path, "data.xlsx"), workbook.ToArray());
        var content = """{"name":"sample","resources":[{"name":"customers","path":"data.xlsx","format":"xlsx","schema":{"fields":[{"name":"id","type":"integer","description":"Keep this"}]}}]}""";
        var source = workspace.Write("package.json", content);
        var result = await CliWorkspace.Run("schema", "infer", source, "--asset", "customers", "--write");
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Output, Does.Contain("Update refused"));
        Assert.That(File.ReadAllText(source), Is.EqualTo(content));
    }
}
