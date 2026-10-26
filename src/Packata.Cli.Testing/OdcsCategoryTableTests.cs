using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

public class OdcsCategoryTableTests
{
    [Test]
    public async Task Validate_reports_invalid_category_metadata_without_loading_lookups()
    {
        using var workspace = new CliWorkspace();
        var descriptor = JObject.Parse(Descriptor);
        descriptor["schema"]![1]!["properties"]![0]!["required"] = false;
        var source = workspace.Write("contract.yaml", descriptor.ToString());
        var result = await CliWorkspace.Run("contract", "validate", source);
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Output + result.Error, Does.Contain("required: true"));
    }

    [TestCase(false, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, false, false)]
    [TestCase(true, true, false)]
    [TestCase(false, true, true)]
    public async Task Preview_uses_ODCS_lookup_bindings_and_preserves_sources(bool lookupFirst, bool decode, bool labelEncoded)
    {
        using var workspace = new CliWorkspace();
        var descriptor = JObject.Parse(Descriptor);
        if (labelEncoded)
            descriptor["schema"]![0]!["properties"]![0]!["customProperties"]![0]!["value"]!["encodedAs"] = "label";
        if (lookupFirst) descriptor["schema"] = new JArray(((JArray)descriptor["schema"]!).Reverse().Select(node => node.DeepClone()));
        var text = descriptor.ToString();
        var source = workspace.Write("contract.odcs.yaml", text);
        var csv = labelEncoded ? "code,other\r\nRegular,STU\r\nX,HON\r\n" : "code,other\r\nREG,STU\r\nX,HON\r\n";
        var data = workspace.Write("members.csv", csv);
        const string lookupText = "key,title\r\nREG,Regular\r\nSTU,Student\r\nHON,\r\n";
        var lookup = workspace.Write("categories.csv", lookupText);
        var args = new List<string> { "data", "preview", source, "--asset", "members", "--output-format", "json" };
        if (decode) args.Add("--decode-categories");
        var result = await CliWorkspace.Run(args.ToArray());
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var rows = JObject.Parse(result.Output)["rows"]!;
        Assert.That((string?)rows[0]![0], Is.EqualTo(decode || labelEncoded ? "Regular" : "REG"));
        Assert.That((string?)rows[0]![1], Is.EqualTo(decode ? "Student" : "STU"));
        Assert.That((string?)rows[1]![0], Is.EqualTo("X"));
        Assert.That((string?)rows[1]![1], Is.EqualTo(decode ? "" : "HON"));
        Assert.That(File.ReadAllText(source), Is.EqualTo(text));
        Assert.That(File.ReadAllText(data), Is.EqualTo(csv));
        Assert.That(File.ReadAllText(lookup), Is.EqualTo(lookupText));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Raw_preview_never_opens_missing_lookups_and_label_encoding_skips_them(bool labelEncoded)
    {
        using var workspace = new CliWorkspace();
        var descriptor = JObject.Parse(Descriptor);
        foreach (var field in descriptor["schema"]![0]!["properties"]!)
            if (labelEncoded) field["customProperties"]![0]!["value"]!["encodedAs"] = "label";
        var source = workspace.Write("contract.yaml", descriptor.ToString());
        workspace.Write("members.csv", "code,other\r\nRegular,Student\r\n");
        var args = new List<string> { "data", "preview", source, "--asset", "members", "--output-format", "json" };
        if (labelEncoded) args.Add("--decode-categories");
        var result = await CliWorkspace.Run(args.ToArray());
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That((string?)JObject.Parse(result.Output)["rows"]![0]![0], Is.EqualTo("Regular"));
    }

    [TestCase("rows", "duplicate mapped value")]
    [TestCase("column", "Mapped column")]
    [TestCase("target", "Category table")]
    [TestCase("external", "external-package")]
    public async Task Preview_reports_shared_lookup_and_ODCS_mapping_diagnostics(string kind, string expected)
    {
        using var workspace = new CliWorkspace();
        var descriptor = JObject.Parse(Descriptor);
        if (kind == "column") descriptor["schema"]![1]!["customProperties"]![0]!["value"]!["value"] = "absent";
        if (kind == "target") descriptor["schema"]![0]!["properties"]![0]!["customProperties"]![0]!["value"]!["resource"] = "absent";
        if (kind == "external") descriptor["schema"]![0]!["properties"]![0]!["customProperties"]![0]!["value"]!["package"] = "another-package";
        var source = workspace.Write("contract.yaml", descriptor.ToString());
        workspace.Write("members.csv", "code,other\r\nREG,STU\r\n");
        workspace.Write("categories.csv", "key,title\r\nREG,First\r\nREG,Second\r\n");
        var result = await CliWorkspace.Run("data", "preview", source, "--asset", "members", "--decode-categories");
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain(expected));
    }

    private const string Descriptor = """
        {"apiVersion":"v3.2.0","kind":"DataContract","id":"members","version":"1.0.0","status":"draft",
        "servers":[{"server":"files","type":"local","path":"./","format":"csv"}],
        "schema":[{"name":"members","physicalName":"members.csv","physicalType":"table","properties":[
        {"name":"code","logicalType":"string","customProperties":[{"vendor":"packata","property":"categories","value":{"resource":"categories"}}]},
        {"name":"other","logicalType":"string","customProperties":[{"vendor":"packata","property":"categories","value":{"resource":"categories"}}]}]},
        {"name":"categories","physicalName":"categories.csv","physicalType":"table",
        "customProperties":[{"vendor":"packata","property":"categoryFieldMap","value":{"value":"key","label":"title"}}],
        "properties":[{"name":"key","logicalType":"string","required":true,"unique":true},
        {"name":"title","logicalType":"string","unique":true}]}]}
        """;
}
