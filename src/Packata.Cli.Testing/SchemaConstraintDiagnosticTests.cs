using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Packata.Cli.Testing;

public class SchemaConstraintDiagnosticTests
{
    [TestCase("avro")]
    [TestCase("spark-json")]
    public async Task String_enum_labels_export_as_compact_code_keyed_metadata(string format)
    {
        using var workspace = new CliWorkspace();
        const string descriptor = """
            apiVersion: v3.2.0
            kind: DataContract
            id: membership
            schema:
              - name: members
                properties:
                  - name: category_code
                    logicalType: string
                    required: true
                    enum:
                      - value: STD
                        label: Standard
                      - value: PRE
                        label: Premium
                      - value: HON
                        label: Honorary
            """;
        var source = workspace.Write("labels.yaml", descriptor);
        var result = await CliWorkspace.Run("schema", "export", source, "--output-format", format);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var field = JObject.Parse(result.Output)["fields"]![0]!;
        var metadata = format == "avro" ? field["packata"]!["field"]!
            : JObject.Parse(field["metadata"]!["packata"]!["field"]!.Value<string>()!);
        var domain = metadata["valueDomain"]!;
        Assert.That(domain["labels"]!["STD"]!.Value<string>(), Is.EqualTo("Standard"));
        Assert.That(domain["labels"]!["PRE"]!.Value<string>(), Is.EqualTo("Premium"));
        Assert.That(domain["labels"]!["HON"]!.Value<string>(), Is.EqualTo("Honorary"));
        Assert.That(domain["entries"], Is.Null);
        Assert.That(domain["allowedValues"], Is.Null);
        Assert.That(File.ReadAllText(source), Is.EqualTo(descriptor));
    }

    private const string Members = """
        apiVersion: v3.2.0
        kind: DataContract
        id: membership
        schema:
          - name: members
            properties:
              - name: member_id
                logicalType: integer
                primaryKey: true
                unique: true
                required: true
              - name: category_code
                logicalType: integer
                enum:
                  - value: 1
                  - value: 2
        """;

    [TestCase("avro")]
    [TestCase("spark-json")]
    public async Task Format_limits_are_informational_and_constraints_survive_export(string format)
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("members.odcs.yaml", Members);
        var result = await CliWorkspace.Run("schema", "export", source, "--output-format", format);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Error, Does.Contain("SCHEMA_RELATIONAL Information").And.Contain("SCHEMA_ENUM_FORMAT_LIMIT Information"));
        Assert.That(result.Error, Does.Not.Contain(" Warning ").And.Not.Contain(" Error "));
        var schema = JObject.Parse(result.Output);
        var metadata = format == "avro" ? schema["fields"]![0]!["packata"]!["field"]!
            : JObject.Parse(schema["fields"]![0]!["metadata"]!["packata"]!["field"]!.Value<string>()!);
        Assert.That(metadata["constraints"]!.Any(c => c["kind"]!.Value<string>() == "unique"), Is.True);
        Assert.That(File.ReadAllText(source), Is.EqualTo(Members));
    }

    [Test]
    public async Task Compatible_Avro_enum_is_emitted_through_the_canonical_command()
    {
        using var workspace = new CliWorkspace();
        var source = workspace.Write("members.yaml", Members.ReplaceLineEndings("\n").Replace("logicalType: integer\n        enum:", "logicalType: string\n        enum:")
            .Replace("value: 1", "value: GOLD").Replace("value: 2", "value: SILVER"));
        var result = await CliWorkspace.Run("schema", "export", source, "--output-format", "avro");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        var field = JObject.Parse(result.Output)["fields"]![1]!;
        Assert.That(field["type"]!["type"]!.Value<string>(), Is.EqualTo("enum"));
        Assert.That(field["type"]!["symbols"]!.Values<string>(), Is.EqualTo(new[] { "GOLD", "SILVER" }));
        Assert.That(result.Error, Does.Not.Contain("SCHEMA_ENUM_FORMAT_LIMIT").And.Not.Contain("SCHEMA_IMPLEMENTATION_GAP"));
    }
}
