using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Execution;
using Packata.Core.Reading;
using Packata.ResourceReaders;
using Packata.Testing;

namespace Packata.Application.Testing;

public sealed class WorkflowServiceTests
{
    private const string Package = """
        resources:
          - name: orders
            description: Keep this description
            path: orders.csv
            format: csv
            schema:
              fields:
                - name: id
                  type: string
                  description: Authored field
                  constraints:
                    required: true
        """;
    private const string Quality = """
        apiVersion: v3.2.0
        kind: DataContract
        id: orders-contract
        version: 1.0.0
        status: draft
        schema:
          - name: orders
            physicalType: file
            quality:
              - id: count
                type: library
                metric: rowCount
                mustBe: 2
            properties:
              - name: id
                logicalType: integer
        servers:
          - server: local
            type: local
            path: orders.csv
            format: csv
        """;

    [TestCase("csv")]
    [TestCase("xlsx")]
    [TestCase("parquet")]
    public async Task FileInference_UsesInjectedStreamsAndReturnsMetadataWithoutWriting(string format)
    {
        var text = Package.Replace("orders.csv", "orders." + format).Replace("format: csv", "format: " + format);
        var inspection = ContractInspector.Inspect(text);
        var contract = inspection.Mapping.RequireValue();
        var asset = contract.Assets.Single();
        var endpoint = ContractInferenceService.SelectEndpoint(contract, asset);
        byte[] data;
        if (format == "xlsx")
        {
            using var workbook = ExcelWorkbookFixture.Create([["id"], [1], [2]]);
            data = workbook.ToArray();
        }
        else data = format == "parquet" ? await File.ReadAllBytesAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Resources", "iris.parquet"))
            : "id\r\n1\r\n2\r\n"u8.ToArray();
        var streams = new InferenceStreams(data);
        var result = await ContractInferenceService.InferFileAsync(new(contract, streams), asset, endpoint,
            new() { Header = "present", StagingMaximumBytes = 1024 * 1024 }, CancellationToken.None);
        Assert.That(result.Result.IsSuccessful, Is.True, string.Join(";", result.Result.Diagnostics));
        Assert.That(result.Asset.Schema!.Fields, Is.Not.Empty);
        Assert.That(streams.Opens, Is.EqualTo(1));
        Assert.That(streams.LastStream!.Disposed, Is.True);
        if (format == "xlsx") Assert.That(result.Asset.Schema.Fields[0].Required, Is.EqualTo(asset.Schema!.Fields[0].Required));
        if (format == "csv")
        {
            var update = DescriptorUpdateService.Update(new(text, DescriptorDocumentKind.DataPackageYaml), asset, endpoint, result.Asset, result.Endpoint);
            Assert.That(update.Content, Does.Contain("Authored field").And.Contain("Keep this description"));
            Assert.That(ContractInspector.Inspect(update.Content).Mapping.RequireValue().Assets.Single().Schema!.Fields.Single().LogicalType,
                Is.EqualTo(result.Asset.Schema.Fields.Single().LogicalType));
        }
    }

    [Test]
    public async Task DialectInference_EnforcesSavePoliciesAndUpdatesOnlyDialect()
    {
        var contract = ContractInspector.Inspect(Package).Mapping.RequireValue();
        var asset = contract.Assets.Single();
        var endpoint = ContractInferenceService.SelectEndpoint(contract, asset);
        var result = await DialectInferenceService.InferAsync(new(contract, new InferenceStreams("id\r\n1\r\n2\r\n"u8.ToArray())), asset, endpoint,
            new() { Header = "present" });
        var document = new EditableContract(Package, DescriptorDocumentKind.DataPackageYaml);
        var update = result.PrepareUpdate(document, asset.Id, endpoint.Id, acceptAmbiguous: true);
        Assert.That(update.Content, Does.Contain("type: string").And.Contain("Authored field"));
        var shared = result with { SharedAssets = ["orders", "other"] };
        Assert.That(Assert.Throws<DialectUpdateException>(() => shared.PrepareUpdate(document, asset.Id, endpoint.Id))!.Failure,
            Is.EqualTo(DialectUpdateFailure.SharedEndpoint));
        Assert.Throws<OperationCanceledException>(() => result.PrepareUpdate(document, asset.Id, endpoint.Id, cancellationToken: new(true)));
    }

    [Test]
    public void ExportAndComparison_AreMetadataOnlyAndRetainNativeProvenance()
    {
        var document = ContractDocument.Parse(Package, baseUri: new Uri("unavailable://contracts/"));
        var json = ContractExportService.JsonSchema(document, "orders");
        Assert.That(json.IsSuccessful, Is.True);
        Assert.That(JObject.Parse(json.Content!)["properties"]!["id"], Is.Not.Null);
        var mapped = document.Map().RequireValue();
        var dialect = ContractExportService.Dialect(document, mapped.Endpoints.Single().Id);
        Assert.That(dialect.IsSuccessful, Is.True, string.Join(";", dialect.Diagnostics));
        Assert.That(JObject.Parse(dialect.Content!)["delimiter"], Is.Not.Null);
        var baseline = ContractComparisonService.Prepare(document);
        var candidate = ContractComparisonService.Prepare(ContractDocument.Parse(Package.Replace("type: string", "type: integer")));
        Assert.That(baseline.Valid, Is.True);
        Assert.That(baseline.Input!.Descriptor, Does.ContainKey("resources"));
        Assert.That(ContractComparisonService.Diff(baseline.Input.Contract!, candidate.Input!.Contract!).Changes, Is.Not.Empty);
        Assert.That(ContractComparisonService.Compare(baseline.Input, candidate.Input).Completed, Is.True);
        var input = ContractComparisonService.SatisfactionInput(baseline.Input, "buffer:orders");
        var satisfaction = ContractComparisonService.Satisfies(input, input);
        Assert.That(satisfaction.Completed, Is.True);
        Assert.That(input.Source, Is.EqualTo("buffer:orders"));
    }

    [Test]
    public void ComparisonAndExports_ReportInvalidInputAndRespectCancellation()
    {
        var invalid = ContractDocument.Parse("resources: []");
        var prepared = ContractComparisonService.Prepare(invalid);
        Assert.That(prepared.Valid, Is.False);
        Assert.That(prepared.ValidationDiagnostics, Is.Not.Empty);
        var valid = ContractDocument.Parse(Package);
        Assert.Throws<ContractAssetSelectionException>(() => ContractExportService.JsonSchema(valid, "missing"));
        var canceled = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => ContractExportService.JsonSchema(valid, "orders", canceled));
        Assert.Throws<OperationCanceledException>(() => ContractComparisonService.Prepare(valid, canceled));
    }

    [Test]
    public async Task InferenceCancellationAndUnsupportedFormats_DoNotAcquireData()
    {
        var contract = ContractInspector.Inspect(Package).Mapping.RequireValue();
        var asset = contract.Assets.Single();
        var endpoint = contract.Endpoints.Single();
        var streams = new InferenceStreams([]);
        await Assert.ThrowsAsync<OperationCanceledException>(() => ContractInferenceService.InferFileAsync(new(contract, streams), asset, endpoint, new(), new(true)));
        await Assert.ThrowsAsync<NotSupportedException>(() => ContractInferenceService.InferFileAsync(new(contract, streams), asset,
            endpoint with { Format = new("json"), Location = new PathLocation(["orders.json"]) }, new(), default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ContractInferenceService.InferFileAsync(new(contract, streams), asset,
            endpoint, new() { MaximumRecords = 0 }, default));
        Assert.That(streams.Opens, Is.Zero);
    }

    [Test]
    public void StandaloneNormalization_IsIndependentOfFilesystemAndConsole()
    {
        var contract = ContractInspector.Inspect(Package).Mapping.RequireValue();
        var source = new EditableContract(Package, DescriptorDocumentKind.DataPackageYaml);
        var normalized = StandaloneContractService.Prepare(source, contract, new Uri("https://example.test/contracts/"), false);
        Assert.That(normalized.Content, Does.Contain("https://example.test/contracts/orders.csv"));
        Assert.That(normalized.Warnings, Has.Count.EqualTo(1));
        Assert.Throws<NotSupportedException>(() => StandaloneContractService.Prepare(source, contract, new Uri("file:///archive/"), true));
    }

    [Test]
    public async Task QualityWorkflow_SelectsExecutesAndProducesValidatedReportsWithoutCli()
    {
        var readers = new PreviewStreams();
        var preparation = QualityExecutionService.Prepare(ContractDocument.Parse(Quality));
        Assert.That(preparation.IsSuccessful, Is.True);
        var selected = QualityExecutionService.SelectRules(preparation.Contract!, new());
        Assert.That(selected, Has.Count.EqualTo(1));
        Assert.That(readers.Opens, Is.Zero);
        var service = new QualityExecutionService(QualityExecutionService.CreateEngines(null), ResourceReaderFactory.Create(_ => { }, readers));
        var result = await service.RunAsync(preparation.Contract!, new(), "buffer:orders");
        Assert.That(result.Outcome, Is.EqualTo(RuleOutcome.Passed));
        Assert.That(readers.Opens, Is.EqualTo(1));
        var report = QualityReportService.Create(result, preparation.Contract!, "v3.2.0",
            new("https://example.test/contract.yaml?token=private-test-value", Quality, "test-version", "application-test"), new());
        QualityReportService.Validate(report);
        Assert.That((string?)report["source"]!["process"], Is.EqualTo("application-test"));
        Assert.That(QualityReportService.Serialize(report, "json"), Does.Not.Contain("private-test-value"));
        Assert.That(QualityReportService.Serialize(report, "yaml"), Does.Contain("ObservabilityResults"));
    }

    [Test]
    public async Task QualitySelectionAndCancellation_DoNotOpenReaders()
    {
        var readers = new PreviewStreams();
        var contract = QualityExecutionService.Prepare(ContractDocument.Parse(Quality)).Contract!;
        var service = new QualityExecutionService(QualityExecutionService.CreateEngines(null), ResourceReaderFactory.Create(_ => { }, readers));
        await Assert.ThrowsAsync<QualitySelectionException>(() => service.RunAsync(contract, new() { Id = ["missing"] }, "buffer"));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RunAsync(contract, new(), "buffer", new(true)));
        Assert.That(readers.Opens, Is.Zero);
    }

    private sealed class InferenceStreams(byte[] bytes) : IInferenceStreamSource
    {
        public int Opens { get; private set; }
        public TrackedStream? LastStream { get; private set; }
        public ValueTask<Stream> OpenAsync(DataAsset asset, DataEndpoint endpoint, long stagingMaximumBytes, long? workbookMaximumBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Opens++;
            LastStream = new(bytes);
            return ValueTask.FromResult<Stream>(LastStream);
        }
    }
    private sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class PreviewStreams : IEndpointStreamResolver
    {
        public int Opens { get; private set; }
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            Opens++;
            return ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("id\r\n1\r\n2\r\n")));
        }
    }
}
