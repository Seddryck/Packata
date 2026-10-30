using NUnit.Framework;
using Packata.LanguageServer.Core;

namespace Packata.LanguageServer.Testing;

public sealed class SelectedValidationTests
{
    [Test]
    public void SchemaMismatch_HasTheSharedMessageAndNavigableCategoryRange()
    {
        const string source = """
            resources:
              - name: members
                path: members.csv
                schema:
                  fields:
                    - name: nationality
                      type: string
                      categories: {resource: countries}
                  primaryKey: [nationality]
            """;
        var result = SelectedValidation.Validate(new(Uri, source, 9), ["schema"]);
        var expected = Packata.Documents.Validation.DescriptorValidation.Validate(source,
            layers: Packata.Documents.Validation.ValidationLayers.Schema).Diagnostics.Single();
        Assert.That(result.Valid, Is.False);
        Assert.That(result.Diagnostics, Has.Length.EqualTo(1));
        Assert.That(result.Diagnostics[0].Message, Is.EqualTo(expected.Message));
        Assert.That(result.Diagnostics[0].SourcePath, Is.EqualTo(expected.SourcePath));
        Assert.That(result.Diagnostics[0].Range, Is.Not.Null);
        Assert.That(result.Diagnostics[0].Range!.Start.Line, Is.EqualTo(7));
    }

    [Test]
    public void RecipeProfile_IsSelectedForUnsavedYaml()
    {
        var profile = Packata.Documents.Validation.DescriptorProfiles.CategoryTablesUri;
        var source = $"$schema: {profile}\nresources:\n  - name: members\n    path: absent.csv\n    schema:\n      fields:\n        - name: code\n          type: string\n          categories:\n            resource: codes\n            package: registry-name\n";
        var result = SelectedValidation.Validate(new(Uri, source, 8));
        Assert.That(result.Valid, Is.True, string.Join(";", result.Diagnostics));
        Assert.That(result.Profile!.Uri, Is.EqualTo(profile));
        Assert.That(result.Version, Is.EqualTo(8));
    }

    private static readonly Uri Uri = new("file:///workspace/orders.odcs.yaml");

    [TestCase(ContractServiceTests.Odcs, "odcs")]
    [TestCase(ContractServiceTests.Package, "datapackage")]
    public void FormatsShareCatalogAndExplicitSelection(string source, string format)
    {
        var snapshot = new DocumentSnapshot(Uri, source, 3);
        var catalog = SelectedValidation.Options(snapshot);
        Assert.That(catalog.Format, Is.EqualTo(format));
        Assert.That(catalog.Validators.Select(v => v.Id), Is.EqualTo(new[] { "schema", "semantic" }));
        foreach (var check in catalog.Validators)
        {
            var result = SelectedValidation.Validate(snapshot, [check.Id]);
            Assert.That(result.Valid, Is.True, string.Join(";", result.Diagnostics));
            Assert.That(result.Checks!.Single(c => c.Id != check.Id).State, Is.EqualTo("NotSelected"));
            Assert.That(result.Checks!.Single(c => c.Id == check.Id).State, Is.EqualTo("Passed"));
        }
    }

    [Test]
    public void SchemaFailureBlocksOnlySelectedSemanticChecks()
    {
        var snapshot = new DocumentSnapshot(Uri, ContractServiceTests.Odcs + "\nunknownRoot: no\n", 1);
        var combined = SelectedValidation.Validate(snapshot);
        Assert.That(combined.Checks!.Select(c => c.State), Is.EqualTo(new[] { "Failed", "Blocked" }));
        Assert.That(combined.Diagnostics.All(d => d.Layer == "Schema"), Is.True);
        var semantic = SelectedValidation.Validate(snapshot, ["semantic"]);
        Assert.That(semantic.Checks!.Single(c => c.Id == "schema").State, Is.EqualTo("NotSelected"));
        Assert.That(semantic.Valid, Is.True);
    }

    [Test]
    public void MissingVectorOptionsFailSemanticsWithNavigableCategory()
    {
        var snapshot = new DocumentSnapshot(Uri, ContractServiceTests.Odcs.Replace("logicalType: integer", "logicalType: vector"), 1);
        var result = SelectedValidation.Validate(snapshot, ["semantic"]);
        Assert.That(result.Valid, Is.False);
        Assert.That(result.Diagnostics.Any(d => d.Layer == "Semantic" && d.SourcePath.Contains("dimensions")), Is.True);
        Assert.That(result.Diagnostics.Single(d => d.Code == "SEM_VECTOR").Range, Is.Not.Null);
        // Existing source locations are indexed without requiring native deserialization.
        var index = new SourceIndex(Packata.Documents.ContractDocument.ParseRoot(snapshot.Text));
        Assert.That(index.DiagnosticRange("$/schema/0/properties/0/logicalType"), Is.Not.Null);
    }

    [TestCase("schema: [")]
    [TestCase("{\"resources\":broken}")]
    [TestCase("apiVersion: v3.2.0\nkind: DataContract\nid: !unknown value")]
    public void SyntaxIsAPrerequisiteAndNeverProducesAnUnqualifiedSuccess(string source)
    {
        var result = SelectedValidation.Validate(new(Uri, source, 4), ["semantic"]);
        Assert.That(result.Valid, Is.False);
        Assert.That(result.Checks!.Single(c => c.Id == "semantic").State, Is.EqualTo("Blocked"));
        Assert.That(result.Checks!.Single(c => c.Id == "schema").State, Is.EqualTo("NotSelected"));
        Assert.That(result.Diagnostics.Single().Layer, Is.EqualTo("Syntax"));
    }

    [Test]
    public void EmptyOrUnknownSelectionsCannotExecute()
    {
        var snapshot = new DocumentSnapshot(Uri, ContractServiceTests.Odcs, 1);
        Assert.Throws<ArgumentException>(() => SelectedValidation.Validate(snapshot, []));
        Assert.Throws<ArgumentException>(() => SelectedValidation.Validate(snapshot, ["unknown"]));
    }

    [Test]
    public async Task RerunsUseUnsavedVersionsAndReplaceScopeAsync()
    {
        var store = new DocumentStore();
        store.Update(Uri, ContractServiceTests.Odcs + "\nunknownRoot: no", 5);
        var service = new ContractService(store);
        var schema = await service.ValidateAsync(Uri, CancellationToken.None, ["schema"]);
        var semantic = await service.ValidateAsync(Uri, CancellationToken.None, ["semantic"]);
        Assert.That(schema.Diagnostics, Is.Not.Empty);
        Assert.That(semantic.Diagnostics, Is.Empty);
        Assert.That(semantic.Version, Is.EqualTo(5));
        var automatic = await service.SyntaxAsync(Uri, CancellationToken.None);
        Assert.That(automatic.Diagnostics, Is.Empty, "Background analysis runs syntax only, never implicitly enabling selected checks.");
    }
}
