using NUnit.Framework;
using Packata.LanguageServer.Core;

namespace Packata.LanguageServer.Testing;

public sealed class SelectedValidationTests
{
    [TestCase("{resources: []}")]
    [TestCase("{\"resources\":[],}")]
    [TestCase("resources: []")]
    [TestCase("name: private-test-value\nschema: [")]
    public void SyntaxDiagnostics_ProjectApplicationResultsAndLocations(string text)
    {
        var application = Packata.Application.Validation.DescriptorSyntaxValidation.Validate(text);
        var editor = SelectedValidation.SyntaxDiagnostics(text);
        Assert.That(editor.Select(value => (value.Code, value.Severity, value.SourcePath, value.Message)),
            Is.EqualTo(application.Diagnostics.Select(value => (value.Code, value.Severity.ToString(), value.SourcePath, value.Message))));
        if (application.Diagnostics.FirstOrDefault()?.Location is { } location)
        {
            Assert.That(editor.Single().Range!.Start.Line, Is.EqualTo(location.Line - 1));
            Assert.That(editor.Single().Range!.Start.Character, Is.EqualTo(location.Column - 1));
        }
        Assert.That(editor.All(value => value.Layer == "Syntax"), Is.True);
    }

    [Test]
    public async Task BackgroundSyntax_UsesUnsavedBuffersWithoutEnablingValidationLayersAsync()
    {
        const string text = "apiVersion: unsupported\nkind: DataContract\nschema: still-editing\n";
        var store = new DocumentStore();
        store.Update(Uri, text, 21);
        var result = await new ContractService(store).SyntaxAsync(Uri, CancellationToken.None);
        Assert.That(result.Valid, Is.True);
        Assert.That(result.Version, Is.EqualTo(21));
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(Packata.Application.Validation.DescriptorValidation.Validate(text).Valid, Is.False);
        await Assert.ThrowsAsync<OperationCanceledException>(() => new ContractService(store).SyntaxAsync(Uri, new(true)));
    }

    [Test]
    public void EditorSizePolicy_RemainsOutsideApplicationSyntaxValidation()
    {
        var text = "description: " + new string('x', ContractService.MaximumDocumentBytes);
        Assert.That(SelectedValidation.SyntaxDiagnostics(text).Single().Code, Is.EqualTo("VAL_SIZE"));
        Assert.That(Packata.Application.Validation.DescriptorSyntaxValidation.Validate(text).Valid, Is.True);
    }

    [TestCase("apiVersion: v3.2.0\nid: contract\nversion: 1.0.0\nstatus: draft\nschema: []", "odcs")]
    [TestCase("kind: DataContract\nschema: []", "odcs")]
    [TestCase("resources: []", "datapackage")]
    public void PartialDocumentFormat_AgreesWithApplicationInspectionAndValidation(string text, string expected)
    {
        var snapshot = new DocumentSnapshot(Uri, text, 17);
        var application = Packata.Application.ContractInspector.Inspect(text);
        var validation = Packata.Application.Validation.DescriptorValidation.Validate(text);
        var editor = SelectedValidation.Validate(snapshot);
        var inspection = new ContractService(new DocumentStore()).Inspect(snapshot, CancellationToken.None);
        Assert.That(application.Document.Format, Is.EqualTo(expected));
        Assert.That(validation.Format, Is.EqualTo(expected));
        Assert.That(SelectedValidation.Options(snapshot).Format, Is.EqualTo(expected));
        Assert.That(editor.Format, Is.EqualTo(expected));
        Assert.That(inspection.Format, Is.EqualTo(expected));
        Assert.That(editor.Version, Is.EqualTo(17));
    }

    [TestCase("schema: not-an-array", "Blocked")]
    [TestCase("schema: []", "Passed")]
    [TestCase("schema: [", "Blocked")]
    public void CheckPresentation_UsesApplicationPrerequisiteStatus(string schema, string expected)
    {
        var text = "apiVersion: v3.2.0\nkind: DataContract\nid: contract\nversion: 1.0.0\nstatus: draft\n" + schema;
        var report = Packata.Application.Validation.DescriptorValidation.Validate(text, layers: Packata.Application.Validation.ValidationLayers.Semantic);
        var editor = SelectedValidation.Validate(new(Uri, text, 1), ["semantic"]);
        var outcome = report.Layers.Single();
        Assert.That(editor.Checks!.Single(check => check.Id == "semantic").State, Is.EqualTo(expected));
        Assert.That(expected == "Blocked", Is.EqualTo(outcome.Prerequisites == Packata.Application.Validation.ValidationPrerequisiteStatus.Failed));
    }

    [Test]
    public void NativeTreeFallback_UsesApplicationStandardWhenKindIsMissing()
    {
        const string text = "apiVersion: v3.2.0\nschema: still-editing\nx-extra: preserved\n";
        var result = new ContractService(new DocumentStore()).Inspect(new(Uri, text, 8), CancellationToken.None);
        Assert.That(result.Format, Is.EqualTo(Packata.Application.DescriptorFormat.Detect(text)));
        Assert.That(result.Format, Is.EqualTo("odcs"));
        Assert.That(result.NativeTree, Is.Not.Null);
    }

    [TestCase(ContractServiceTests.Package)]
    [TestCase(ContractServiceTests.Odcs)]
    [TestCase("resources: []")]
    public void Validation_PreservesApplicationOutcomesBeforeEditorProjection(string text)
    {
        var snapshot = new DocumentSnapshot(Uri, text, 12);
        var application = Packata.Application.Validation.DescriptorValidation.Validate(text, baseUri: new Uri(Uri, "."));
        var editor = SelectedValidation.Validate(snapshot);
        Assert.Multiple(() =>
        {
            Assert.That(editor.Valid, Is.EqualTo(application.Valid));
            Assert.That(editor.Profile, Is.EqualTo(application.Profile));
            Assert.That(editor.Version, Is.EqualTo(12));
            Assert.That(editor.Diagnostics.Select(value => (value.Code, value.Severity, value.SourcePath, value.Message)),
                Is.EqualTo(application.Diagnostics.Select(value => (value.Code, value.Severity.ToString(),
                    Packata.Application.ContractJson.Redact(value.SourcePath), Packata.Application.ContractJson.Redact(value.Message))).Distinct()));
        });
    }

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
        var expected = Packata.Application.Validation.DescriptorValidation.Validate(source,
            layers: Packata.Application.Validation.ValidationLayers.Schema).Diagnostics.Single();
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
        var profile = Packata.Application.Validation.DescriptorProfiles.CategoryTablesUri;
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
        var index = new SourceIndex(Packata.Application.ContractDocument.ParseRoot(snapshot.Text));
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
