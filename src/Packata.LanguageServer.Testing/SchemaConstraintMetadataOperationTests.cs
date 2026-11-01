using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Application.Generation;
using Packata.LanguageServer.Core;

namespace Packata.LanguageServer.Testing;

public class SchemaConstraintMetadataOperationTests
{
    private const string Descriptor = """
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
              - name: category
                logicalType: string
                enum:
                  - value: GOLD
                    label: Gold
                  - value: SILVER
                    label: Silver
        """;

    [TestCase("avro")]
    [TestCase("spark-json")]
    public async Task Metadata_native_enums_and_information_severity_reach_editor_hostsAsync(string format)
    {
        var uri = new Uri("file:///workspace/members.yaml");
        var documents = new DocumentStore();
        documents.Update(uri, Descriptor, 7);
        var operations = new SchemaGenerationOperations(documents);
        var request = new SchemaGenerationRequest(uri.AbsoluteUri, SchemaGenerationOperations.Key(Descriptor),
            new(AssetId: "members", Operation: "export", OutputFormat: format), 7, "/schema/0");
        var response = await operations.GenerateAsync(request, default);
        Assert.That(response.Result.IsSuccessful, Is.True, string.Join(";", response.Result.Diagnostics));
        Assert.That(response.Version, Is.EqualTo(7));
        Assert.That(response.Result.Diagnostics.Any(d => d.Severity == "Warning"), Is.False);
        Assert.That(response.Findings.Any(f => f.Diagnostic.Code == "SCHEMA_RELATIONAL" && f.Diagnostic.Severity == "Information"), Is.True);
        var json = JObject.Parse(response.Result.Content!);
        var metadata = format == "avro" ? json["packata"]!["schema"]!
            : JObject.Parse(json["fields"]![0]!["metadata"]!["packata"]!["schema"]!.Value<string>()!);
        Assert.That(metadata["primaryKey"]!.Values<string>(), Is.EqualTo(new[] { "member_id" }));
        var field = json["fields"]![1]!;
        var fieldMetadata = format == "avro" ? field["packata"]!["field"]!
            : JObject.Parse(field["metadata"]!["packata"]!["field"]!.Value<string>()!);
        Assert.That(fieldMetadata["valueDomain"]!["labels"]!["GOLD"]!.Value<string>(), Is.EqualTo("Gold"));
        Assert.That(fieldMetadata["valueDomain"]!["entries"], Is.Null);
        if (format == "avro")
        {
            Assert.That(json["fields"]![1]!["type"]!["symbols"]!.Values<string>(), Is.EqualTo(new[] { "GOLD", "SILVER" }));
            Assert.That(response.Result.Diagnostics.Any(d => d.Code == "SCHEMA_ENUM_FORMAT_LIMIT"), Is.False);
        }
        else
        {
            Assert.That(response.Findings.Any(f => f.Diagnostic.Code == "SCHEMA_ENUM_FORMAT_LIMIT" && f.Diagnostic.Severity == "Information"), Is.True);
        }
    }
}
