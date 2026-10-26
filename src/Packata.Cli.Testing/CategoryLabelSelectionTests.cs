using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

public class CategoryLabelSelectionTests
{
    [Test]
    public async Task Preview_falls_back_for_declared_missing_translations_and_preserves_empty_labels()
    {
        using var workspace = new CliWorkspace();
        var descriptor = JObject.Parse(Descriptor);
        var fields = (JArray)descriptor["resources"]![1]!["schema"]!["fields"]!;
        fields[3]!["missingValues"] = new JArray("~");
        var source = workspace.Write("datapackage.json", descriptor.ToString());
        workspace.Write("data.csv", "code\r\nN\r\nS\r\n");
        workspace.Write("lookup.csv", "key,title,german,libelle,short,description\r\nN,North,Norden,~,N.,Description\r\nS,South,Süden,,S.,Description\r\n");
        var result = await CliWorkspace.Run("data", "preview", source, "--asset", "data", "--output-format", "json",
            "--decode-categories-language", "fr");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var rows = JObject.Parse(result.Output)["rows"]!;
        Assert.That((string?)rows[0]![0], Is.EqualTo("North"));
        Assert.That((string?)rows[1]![0], Is.Empty);
    }

    [TestCase("--decode-categories-language", "de", "Norden", false)]
    [TestCase("--decode-categories-language", "FR-be", "Nord", true)]
    [TestCase("--decode-categories-language", "es", "North", false)]
    [TestCase("--decode-categories-label", "libelle", "Nord", false)]
    [TestCase("--decode-categories-label", "short", "N.", true)]
    public async Task Preview_selects_labels_and_keeps_sources(string selector, string value, string expected, bool flag)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("datapackage.json", Descriptor);
        var data = workspace.Write("data.csv", "code\r\nN\r\nX\r\n");
        const string lookupText = "key,title,german,libelle,short,description\r\nN,North,Norden,Nord,N.,Description\r\n";
        var lookup = workspace.Write("lookup.csv", lookupText);
        var args = new List<string> { "data", "preview", source, "--asset", "data", "--output-format", "json", selector, value };
        if (flag) args.Add("--decode-categories");
        var result = await CliWorkspace.Run(args.ToArray());
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var rows = JObject.Parse(result.Output)["rows"]!;
        Assert.That((string?)rows[0]![0], Is.EqualTo(expected));
        Assert.That((string?)rows[1]![0], Is.EqualTo("X"));
        if (value == "es") Assert.That(result.Error, Does.Contain("no label for language"));
        Assert.That(File.ReadAllText(source), Is.EqualTo(Descriptor));
        Assert.That(File.ReadAllText(lookup), Is.EqualTo(lookupText));
        Assert.That(File.ReadAllText(data), Is.EqualTo("code\r\nN\r\nX\r\n"));
    }

    [TestCase("both", "mutually exclusive")]
    [TestCase("language", "language tag")]
    [TestCase("column", "not a declared label")]
    [TestCase("undeclared", "not a declared label")]
    public async Task Preview_reports_actionable_selector_errors(string kind, string expected)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("datapackage.json", Descriptor);
        workspace.Write("data.csv", "code\r\nN\r\n");
        workspace.Write("lookup.csv", "key,title,german,libelle,short,description\r\nN,North,Norden,Nord,N.,Description\r\n");
        string[] selector = kind switch
        {
            "both" => ["--decode-categories-language", "fr", "--decode-categories-label", "libelle"],
            "language" => ["--decode-categories-language", "label_de"],
            "undeclared" => ["--decode-categories-label", "description"],
            _ => ["--decode-categories-label", "missing"]
        };
        var result = await CliWorkspace.Run(["data", "preview", source, "--asset", "data", .. selector]);
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Error, Does.Contain(expected));
        Assert.That(result.Output, Is.Empty);
    }

    private const string Descriptor = """
        {"resources":[{"name":"data","path":"data.csv","format":"csv","schema":{"fields":[
          {"name":"code","type":"string","categories":{"resource":"lookup"}}]}},
          {"name":"lookup","type":"table","path":"lookup.csv","format":"csv",
          "categoryFieldMap":{"value":"key","label":"title","labels":["german","libelle","short"]},"schema":{"fields":[
          {"name":"key","type":"string","constraints":{"required":true,"unique":true}},
          {"name":"title","type":"string","language":"en","constraints":{"unique":true}},
          {"name":"german","type":"string","language":"de"},
          {"name":"libelle","type":"string","language":"fr"},
          {"name":"short","type":"string"},
          {"name":"description","type":"string","language":"fr"}]}}]}
        """;
}
