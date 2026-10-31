using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Application.Generation;
using System.Text.RegularExpressions;
namespace Packata.Application.Testing;
public class SchemaManifestTests
{
    [Test]
    public void Every_bundled_manifest_route_matches_the_Application_catalog()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "vscode-extension", "package.json"))) directory = directory.Parent;
        Assert.That(directory, Is.Not.Null);
        var root = Path.Combine(directory!.FullName, "vscode-extension");
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(root, "package.json")))["contributes"]!;
        var routes = File.ReadAllText(Path.Combine(root, "src", "schemaRoutes.ts"));
        foreach (Match match in Regex.Matches(routes, @"'(?<id>packata.schema.[^']+)': \{ (?<body>[^}]+) \}"))
        {
            // Parse the compact routing literals; presentation labels remain in the manifest.
            var values = Regex.Matches(match.Groups["body"].Value, @"(?<key>\w+): '(?<value>[^']+)'").Cast<Match>().ToDictionary(m => m.Groups["key"].Value, m => m.Groups["value"].Value);
            var targetId = values.GetValueOrDefault("outputFormat") ?? values["target"];
            var target = SchemaGenerationService.Catalog.Targets.SingleOrDefault(t => t.Id == targetId && t.Operation == values["operation"] && (t.Operation == "export" || t.Language == values["language"]));
            Assert.That(target, Is.Not.Null, match.Value);
            if (values.TryGetValue("dialect", out var dialect)) Assert.That(target!.Dialects!.Select(d => d.Id), Does.Contain(dialect));
            Assert.That(manifest["commands"]!.Any(c => c["command"]!.Value<string>() == match.Groups["id"].Value), Is.True);
        }
        Assert.That(Regex.Matches(routes, "operation:").Count, Is.EqualTo(SchemaGenerationService.Catalog.Targets.Count(t => t.Id != "ddl") + SchemaGenerationService.Catalog.Targets.Single(t => t.Id == "ddl").Dialects!.Count));
    }
}
