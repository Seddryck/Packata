using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Application.Validation;
using Packata.Core.Contracts;

namespace Packata.Application.Testing;

public sealed class ContractWorkflowTests
{
    [Test]
    public void Application_DoesNotReferenceEitherHostOrResourceAcquisition()
    {
        var references = typeof(ContractInspector).Assembly.GetReferencedAssemblies().Select(value => value.Name).ToArray();
        Assert.That(references, Has.None.StartsWith("Spectre."));
        Assert.That(references, Has.None.StartsWith("OmniSharp."));
        Assert.That(references, Has.None.StartsWith("Packata.Cli"));
        Assert.That(references, Has.None.StartsWith("Packata.LanguageServer"));
        Assert.That(references, Does.Not.Contain("Packata.Database"));
        Assert.That(references, Does.Not.Contain("Packata.Storages"));
    }

    private const string Package = """
        resources:
          - name: orders
            path: unavailable.csv
            schema:
              fields:
                - name: id
                  type: integer
        """;
    private const string PackageJson = """
        {"resources":[{"name":"orders","path":"unavailable.csv","schema":{"fields":[{"name":"id","type":"integer"}]}}]}
        """;
    private const string Odcs = """
        apiVersion: v3.2.0
        kind: DataContract
        id: orders-contract
        version: 1.0.0
        status: draft
        schema:
          - name: orders
            properties:
              - name: id
                logicalType: integer
        """;

    [TestCase(Package, "datapackage")]
    [TestCase(PackageJson, "datapackage")]
    [TestCase(Odcs, "odcs")]
    public void InspectAndValidate_ShareSourceAndDoNotReadReferencedData(string text, string format)
    {
        // No transport can open this scheme. Declaration workflows must never try to do so.
        var baseUri = new Uri("unavailable://workspace/contracts/");
        var syntax = ContractSyntax.Parse(text);
        var inspection = ContractInspector.Inspect(syntax, baseUri: baseUri);
        var validation = DescriptorValidation.Validate(text, baseUri: baseUri);
        Assert.Multiple(() =>
        {
            Assert.That(syntax.Recognized, Is.True);
            Assert.That(inspection.Document.Root, Is.SameAs(syntax.Root));
            Assert.That(inspection.Document.Format, Is.EqualTo(format));
            Assert.That(inspection.Mapping.IsSuccessful, Is.True);
            Assert.That(inspection.Mapping.RequireValue().Assets.Single().Id, Is.EqualTo("orders"));
            Assert.That(validation.Valid, Is.True, string.Join("; ", validation.Diagnostics));
            Assert.That(validation.Syntax!.Text, Is.EqualTo(text));
            Assert.That(validation.Profile!.Format, Is.EqualTo(format));
        });
        if (inspection.Document.NativeDocument is Packata.DataPackage.DataPackage package)
            Assert.That(package.Resources[0].Paths[0].ToString(), Does.Contain("unavailable.csv"));
    }

    [Test]
    public async Task Inspect_PreservesBaseUriForLaterExplicitReads()
    {
        var directory = Path.Combine(Path.GetTempPath(), "packata-application-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "unavailable.csv"), "id\n42\n");
            var inspection = ContractInspector.Inspect(Package, baseUri: new Uri(directory + Path.DirectorySeparatorChar));
            var package = (Packata.DataPackage.DataPackage)inspection.Document.NativeDocument;
            await using var stream = await package.Resources[0].Paths[0].OpenAsync();
            using var reader = new StreamReader(stream);
            Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("id\n42\n"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public void Inspect_RespectsExplicitFormatOverride()
    {
        var syntax = ContractSyntax.Parse("kind: unrelated\n" + Package);
        Assert.That(ContractInspector.Inspect(syntax, "datapackage").Document.Format, Is.EqualTo("datapackage"));
        Assert.Throws<ArgumentException>(() => ContractInspector.Inspect(Package, "unknown"));
    }

    [TestCase("schema: [")]
    [TestCase("a: &cycle [*cycle]")]
    [TestCase("---\nresources: []\n---\nresources: []")]
    public void MalformedInput_BlocksValidationAndCannotBeInspected(string text)
    {
        Assert.That(() => ContractInspector.Inspect(text), Throws.Exception);
        var result = DescriptorValidation.Validate(text);
        Assert.That(result.Valid, Is.False);
        Assert.That(result.Layers.All(layer => layer.State == ValidationState.Skipped), Is.True);
        Assert.That(result.Diagnostics.All(finding => finding.Code == "VAL_PARSE"), Is.True);
    }

    [Test]
    public void ValidationFailure_RetainsSyntaxAndStructuredFindings()
    {
        var result = DescriptorValidation.Validate(Odcs.Replace("id: orders-contract", "id: ''"));
        Assert.That(result.Valid, Is.False);
        Assert.That(result.Syntax, Is.Not.Null);
        Assert.That(result.Diagnostics.Any(finding => finding.Severity == MappingSeverity.Error), Is.True);
    }

    [Test]
    public void Cancellation_StopsBothWorkflowsBeforeParsing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ContractInspector.Inspect("broken: [", cancellationToken: cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => DescriptorValidation.Validate("broken: [", cancellationToken: cancellation.Token));
    }

    [Test]
    public void JsonProjection_RedactsNestedValuesAndKeepsSerializationPolicyStable()
    {
        var projected = ContractJson.RedactedJson(new
        {
            Severity = MappingSeverity.Error,
            CustomProperties = new[] { new { Property = "apiKey", Value = "private-value" } }
        });
        var json = JObject.Parse(ContractJson.Serialize(projected));
        Assert.That(json["severity"]!.Value<string>(), Is.EqualTo("Error"));
        Assert.That(json["customProperties"]![0]!["value"]!.Value<string>(), Is.EqualTo("[redacted]"));
        Assert.That(ContractJson.Serialize(new { Severity = MappingSeverity.Warning }), Does.Contain("\"severity\": \"Warning\""));
    }
}
