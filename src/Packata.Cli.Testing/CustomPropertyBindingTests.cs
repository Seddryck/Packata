using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

public sealed class CustomPropertyBindingTests
{
    [Test]
    public async Task Config_commands_and_inspect_share_effective_bindings()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("contract.yaml", CliWorkspace.Odcs.Replace("    physicalType: file",
            "    customProperties:\n      - {property: additionalProperties, value: false}\n" +
            "      - {property: allowAdditionalProperties, vendor: packata, value: true}\n" +
            "    physicalType: file", StringComparison.Ordinal));
        var environment = new Dictionary<string, string?> { ["PACKATA_CONFIG_HOME"] = workspace.Path };
        const string key = "mapping.odcs.schema.allowAdditionalProperties";
        async Task<JToken> Json(params string[] args)
        {
            var response = await CliWorkspace.RunWithEnvironment(environment, [.. args, "--output-format", "json"]);
            Assert.That(response.ExitCode, Is.Zero, response.Error + response.Output);
            return JToken.Parse(response.Output);
        }

        var listed = await Json("config", "list");
        Assert.That(listed.Count(entry => entry.Value<string>("key")!.StartsWith("mapping.", StringComparison.Ordinal)), Is.EqualTo(16));
        var set = await CliWorkspace.RunWithEnvironment(environment, ["config", "set", key, """{"property":"additionalProperties","vendor":null}"""]);
        Assert.That(set.ExitCode, Is.Zero, set.Error);
        var effective = (await Json("config", "get", key))[0]!;
        Assert.That(effective["value"]!["vendor"]!.Type, Is.EqualTo(JTokenType.Null));
        Assert.That(effective["builtInValue"]!["vendor"]!.Value<string>(), Is.EqualTo("packata"));

        var rejected = await CliWorkspace.RunWithEnvironment(environment,
            ["config", "set", "mapping.odcs.schema.enforcePropertyOrder", """{"property":"additionalProperties","vendor":null}"""]);
        Assert.That(rejected.ExitCode, Is.Not.Zero);
        Assert.That(rejected.Error, Does.Contain("select the same custom property"));

        var mapped = await Json("contract", "inspect", source);
        Assert.That(mapped.SelectToken("$.contract.assets[0].schema.allowAdditionalProperties")!.Value<bool>(), Is.False);
        var reset = await CliWorkspace.RunWithEnvironment(environment, ["config", "unset", key]);
        Assert.That(reset.ExitCode, Is.Zero, reset.Error);
        mapped = await Json("contract", "inspect", source);
        Assert.That(mapped.SelectToken("$.contract.assets[0].schema.allowAdditionalProperties")!.Value<bool>(), Is.True);
    }

    [Test]
    public void Mapping_settings_ignore_unrelated_invalid_connector_values()
    {
        using var workspace = new CliWorkspace();
        var path = workspace.Write("config.json", """{"connectors.catalog":17,"mapping.datapackage.field.defaultValue":"initial"}""");
        var configuration = new CliConfiguration(path);
        Assert.That(configuration.MappingOptions().DataPackage.DefaultValueProperty, Is.EqualTo("initial"));
        Assert.That(() => configuration.Get("connectors.catalog"), Throws.TypeOf<CliException>());
    }

    [TestCase("""{"mapping.datapackage.field.defaultValue":17}""")]
    [TestCase("""{"mapping.datapackage.field.defaultValue":""")]
    public void Mapping_settings_reject_invalid_mapping_objects(string text)
    {
        using var workspace = new CliWorkspace();
        var path = workspace.Write("config.json", text);
        Assert.That(() => new CliConfiguration(path).MappingOptions(), Throws.TypeOf<CliException>());
    }

    [TestCase("mapping.odcs.schema.unknown", """{"vendor":null}""")]
    [TestCase("mapping.odcs.schema.allowAdditionalProperties", """{"property":""}""")]
    [TestCase("mapping.odcs.schema.allowAdditionalProperties", """{"vendor":""}""")]
    [TestCase("mapping.datapackage.field.defaultValue.vendor", "packata")]
    [TestCase("mapping.odcs.schema.allowAdditionalProperties", """{"vendor":null,"vendor":"packata"}""")]
    public void Invalid_configuration_is_rejected_before_creating_a_store(string key, string value)
    {
        using var workspace = new CliWorkspace();
        var path = Path.Combine(workspace.Path, "config.json");
        Assert.That(() => new CliConfiguration(path).Set(key, value), Throws.TypeOf<CliException>());
        Assert.That(File.Exists(path), Is.False);
    }
}
