using NUnit.Framework;
using Packata.Application.Validation;
using Packata.Core.Contracts;

namespace Packata.Application.Testing;

public sealed class BoundaryRegressionTests
{
    public const string Odcs = "apiVersion: v3.2.0\nkind: DataContract\nid: contract\nversion: 1.0.0\nstatus: draft\nschema: []\n";
    private const string Package = "resources:\n  - name: orders\n    path: orders.csv\n  - name: customers\n    path: customers.csv\n";

    [TestCase("csv", InferenceOption.SheetName)]
    [TestCase("tsv", InferenceOption.SheetNumber)]
    [TestCase("psv", InferenceOption.SheetName)]
    [TestCase("xlsx", InferenceOption.Culture)]
    [TestCase("xls", InferenceOption.DialectInference)]
    [TestCase("parquet", InferenceOption.MaximumRecords)]
    [TestCase("parquet", InferenceOption.MaximumBytes)]
    [TestCase("parquet", InferenceOption.Header)]
    [TestCase("pqt", InferenceOption.Culture)]
    public async Task ExplicitUnsupportedOptions_FailBeforeOpeningStreams(string format, InferenceOption option)
    {
        var contract = ContractInspector.Inspect(Package).Mapping.RequireValue();
        var endpoint = contract.Endpoints[0] with { Format = new(format), Location = new PathLocation(["orders." + format]) };
        var streams = new NeverOpen();
        // Even an explicitly supplied default (e.g. Header=auto) must be checked.
        var error = await Assert.ThrowsAsync<UnsupportedInferenceOptionsException>(() => ContractInferenceService.InferFileAsync(
            new(contract, streams), contract.Assets[0], endpoint, new() { SuppliedOptions = option }, default));
        Assert.That(error!.Options, Is.EqualTo(option));
        Assert.That(streams.Opens, Is.Zero);
    }

    [TestCase("csv", "sheet")]
    [TestCase("xlsx", "culture")]
    [TestCase("xlsx", "dialect")]
    [TestCase("parquet", "records")]
    [TestCase("parquet", "bytes")]
    [TestCase("parquet", "header")]
    public async Task NonDefaultUnsupportedValues_AreCheckedWithoutExplicitOptionMetadata(string format, string setting)
    {
        var contract = ContractInspector.Inspect(Package).Mapping.RequireValue();
        var streams = new NeverOpen();
        var options = setting switch
        {
            "sheet" => new ContractInferenceOptions { SheetName = "ThisWorksheetCannotExist" },
            "culture" => new() { Culture = "fr-FR" },
            "dialect" => new() { NoDialectInference = true },
            "records" => new() { MaximumRecords = 2 },
            "bytes" => new() { MaximumBytes = 1024 },
            _ => new() { Header = "present" }
        };
        await Assert.ThrowsAsync<UnsupportedInferenceOptionsException>(() => ContractInferenceService.InferFileAsync(new(contract, streams),
            contract.Assets[0], contract.Endpoints[0] with { Format = new(format) }, options, default));
        Assert.That(streams.Opens, Is.Zero);
    }

    [TestCase("csv", InferenceOption.MaximumRecords | InferenceOption.Culture | InferenceOption.Header | InferenceOption.DialectInference)]
    [TestCase("xlsx", InferenceOption.MaximumRecords | InferenceOption.MaximumBytes | InferenceOption.Header | InferenceOption.SheetName)]
    [TestCase("parquet", InferenceOption.None)]
    public void SupportedOptionSetsAndDefaultsRemainValid(string format, InferenceOption supplied)
        => Assert.DoesNotThrow(() => InferenceCapabilities.Validate(format, new() { SuppliedOptions = supplied }));

    [TestCase("apiVersion: v3.2.0\nschema: []", "odcs")]
    [TestCase("kind: DataContract\nschema: []", "odcs")]
    [TestCase("resources: []", "datapackage")]
    public void PartialDocumentFormat_IsSharedBySyntaxInspectionAndValidation(string text, string expected)
    {
        var syntax = ContractSyntax.Parse(text);
        var report = DescriptorValidation.Validate(text);
        Assert.That(syntax.Format, Is.EqualTo(expected));
        Assert.That(DescriptorFormat.Detect(text), Is.EqualTo(expected));
        Assert.That(ContractInspector.Inspect(syntax).Document.Format, Is.EqualTo(expected));
        Assert.That(report.Format, Is.EqualTo(expected));
        Assert.That(report.Profile?.Format, Is.EqualTo(expected));
    }

    [Test]
    public void ExplicitFormatOverride_IsAppliedConsistently()
    {
        const string text = "apiVersion: irrelevant\nresources: []";
        var syntax = ContractSyntax.Parse(text);
        Assert.That(DescriptorFormat.Identify(syntax.Root, "datapackage"), Is.EqualTo("datapackage"));
        Assert.That(ContractInspector.Inspect(syntax, "datapackage").Document.Format, Is.EqualTo("datapackage"));
        Assert.That(DescriptorValidation.Validate(text, "datapackage").Format, Is.EqualTo("datapackage"));
        Assert.That(DescriptorValidation.Validate("schema: [", "odcs").Format, Is.EqualTo("odcs"));
    }

    [TestCase("apiVersion: v3.2.0\nschema: [", "file:///workspace/custom.yaml", "odcs")]
    [TestCase("schema: [", "file:///workspace/contract.yaml", "odcs")]
    [TestCase("resources: [", "file:///workspace/datapackage.json", "datapackage")]
    public void BrokenBufferFormatHints_AreOwnedByApplication(string text, string uri, string expected)
        => Assert.That(DescriptorFormat.Detect(text, new(uri)), Is.EqualTo(expected));

    [TestCase("schema: [", ValidationState.Skipped, false, ValidationPrerequisiteStatus.Failed)]
    [TestCase("schema: not-an-array", ValidationState.Failed, true, ValidationPrerequisiteStatus.Failed)]
    [TestCase("schema: []", ValidationState.Passed, true, ValidationPrerequisiteStatus.Satisfied)]
    public void PrerequisiteStatus_DistinguishesSkippedPartialAndCompletedChecks(string schema, ValidationState state,
        bool executed, ValidationPrerequisiteStatus prerequisites)
    {
        var text = Odcs.Replace("schema: []", schema);
        var layer = DescriptorValidation.Validate(text, layers: ValidationLayers.Semantic).Layers.Single();
        Assert.That(layer.State, Is.EqualTo(state));
        Assert.That(layer.Executed, Is.EqualTo(executed));
        Assert.That(layer.Prerequisites, Is.EqualTo(prerequisites));
    }

    [Test]
    public void FailedDeclarationWithCompletedChecks_IsNotPrerequisiteFailure()
    {
        var layer = DescriptorValidation.Validate(Odcs.Replace("id: contract", "id: ''"), layers: ValidationLayers.Semantic).Layers.Single();
        Assert.That(layer.State, Is.EqualTo(ValidationState.Failed));
        Assert.That(layer.Prerequisites, Is.EqualTo(ValidationPrerequisiteStatus.Satisfied));
    }

    [Test]
    public void ScopedInspection_PreservesSourceDiagnosticsAndUnscopedContract()
    {
        var document = ContractDocument.Parse(Package);
        var original = ContractInspector.Inspect(document);
        var scoped = ContractInspector.Inspect(document, options: new("orders"));
        var expectedEndpoints = original.Mapping.RequireValue().Assets[0].EndpointBindings.Select(binding => binding.EndpointId).ToArray();
        Assert.That(scoped.Mapping.RequireValue().Assets.Select(asset => asset.Id), Is.EqualTo(new[] { "orders" }));
        Assert.That(scoped.Mapping.Value!.Endpoints.Select(endpoint => endpoint.Id), Is.EqualTo(expectedEndpoints));
        Assert.That(scoped.Document, Is.SameAs(document));
        Assert.That(scoped.Mapping.Diagnostics, Is.EqualTo(original.Mapping.Diagnostics));
        Assert.That(ContractInspector.Inspect(document).Mapping.RequireValue().Assets, Has.Count.EqualTo(2));
        Assert.Throws<ContractAssetSelectionException>(() => ContractInspector.Inspect(document, options: new("missing")));
        var duplicate = ContractDocument.Parse(Package.Replace("name: customers", "name: orders"));
        Assert.That(Assert.Throws<ContractAssetSelectionException>(() => ContractInspector.Inspect(duplicate, options: new("orders")))!.Ambiguous, Is.True);
        Assert.Throws<OperationCanceledException>(() => ContractInspector.Inspect(document, new(true), new("orders")));
    }

    [Test]
    public void ScopedInspection_RetainsSharedEndpointAndSelectsAnUnboundAsset()
    {
        var text = Odcs.Replace("schema: []", "schema:\n  - name: orders\n  - name: customers\n")
            + "servers:\n  - server: shared\n    type: local\n    path: shared.csv\n";
        var document = ContractDocument.Parse(text);
        var scoped = ContractInspector.Inspect(document, options: new("orders")).Mapping.RequireValue();
        Assert.That(scoped.Endpoints.Select(endpoint => endpoint.Id), Is.EqualTo(new[] { "shared" }));
        var unbound = ContractDocument.Parse(text[..text.IndexOf("servers:", StringComparison.Ordinal)]);
        Assert.That(ContractInspector.Inspect(unbound, options: new("orders")).Mapping.RequireValue().Endpoints, Is.Empty);
    }

    private sealed class NeverOpen : IInferenceStreamSource
    {
        public int Opens { get; private set; }
        public ValueTask<Stream> OpenAsync(DataAsset asset, DataEndpoint endpoint, long maximum, long? workbookMaximum, CancellationToken token)
        {
            Opens++;
            throw new AssertionException("Inference attempted resource access before validating its options.");
        }
    }
}
