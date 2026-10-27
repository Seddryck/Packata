using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.LanguageServer.Core;

namespace Packata.LanguageServer.Testing;

[TestFixture]
public sealed class ContractServiceTests
{
    internal const string Package = """
        name: sales
        resources:
          - name: orders
            path: orders.csv
            schema:
              primaryKey: id
              fields:
                - name: id
                  type: integer
                  constraints:
                    required: true
                    minimum: 1
                - name: amount
                  type: number
        """;
    internal const string Odcs = """
        apiVersion: v3.2.0
        kind: DataContract
        id: customer-contract
        name: Customers
        version: 1.0.0
        status: active
        schema:
          - name: customers
            physicalType: table
            properties:
              - name: id
                logicalType: integer
                primaryKey: true
              - name: address
                logicalType: object
                properties:
                  - name: city
                    logicalType: string
        """;

    [TestCase(Package, "datapackage", "orders", "id")]
    [TestCase(Odcs, "odcs", "customers", "city")]
    public async Task Inspect_UsesUnsavedBufferAndBuildsSourceBackedCanonicalTreeAsync(string text, string format, string asset, string field)
    {
        var store = new DocumentStore();
        var uri = new Uri("file:///workspace/folder/unsaved.yaml");
        store.Update(uri, text, 7);
        var result = await new ContractService(store).InspectAsync(uri, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.Recognized, Is.True);
            Assert.That(result.Format, Is.EqualTo(format));
            Assert.That(result.Version, Is.EqualTo(7));
            Assert.That(result.BaseUri, Is.EqualTo("file:///workspace/folder/"));
            Assert.That(result.Contract!["assets"]![0]!["id"]!.Value<string>(), Is.EqualTo(asset));
            Assert.That(Flatten(result.Tree!).Single(node => node.Kind == "field" && node.Label == field).Location, Is.Not.Null);
            Assert.That(Flatten(result.Tree!).Any(node => node.Label == "SLA commitments"), Is.False);
            Assert.That(Flatten(result.Tree!).Any(node => node.Label.Contains("Quality rules")), Is.False);
        });
        var selected = Flatten(result.Tree!).Single(node => node.Kind == "field" && node.Label == field);
        var start = selected.Location!.Range.Start;
        Assert.That(text.Split('\n')[start.Line][start.Character..], Does.StartWith("name: " + field));
    }

    [Test]
    public void Updates_RejectOldVersionsAndInvalidateClosedSnapshots()
    {
        var store = new DocumentStore();
        var uri = new Uri("file:///workspace/test.yaml");
        Assert.That(store.Update(uri, Package, 3), Is.True);
        Assert.That(store.Update(uri, "stale", 2), Is.False);
        Assert.That(store.IsCurrent(new(uri, Package, 3)), Is.True);
        Assert.That(store.IsCurrent(new(uri, Package, 2)), Is.False);
        store.Close(uri);
        Assert.That(store.IsCurrent(new(uri, Package, 3)), Is.False);
    }

    [Test]
    public async Task Discovery_SpansFoldersAndUsesOpenBuffersOverDiskAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "packata-lsp-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var a = Directory.CreateDirectory(Path.Combine(root, "a"));
            var b = Directory.CreateDirectory(Path.Combine(root, "b"));
            var path = Path.Combine(a.FullName, "sales.yaml");
            await File.WriteAllTextAsync(path, Package);
            await File.WriteAllTextAsync(Path.Combine(b.FullName, "customers.yaml"), Odcs);
            await File.WriteAllTextAsync(Path.Combine(b.FullName, "unrelated.json"), "{\"name\":\"app\",\"version\":\"1\"}");
            var ignored = Directory.CreateDirectory(Path.Combine(a.FullName, "node_modules"));
            await File.WriteAllTextAsync(Path.Combine(ignored.FullName, "contract.yaml"), Odcs);
            var store = new DocumentStore();
            store.Update(new Uri(path), Package.Replace("sales", "Unsaved"), 2);
            var service = new ContractService(store);
            var result = await service.DiscoverAsync([new Uri(a.FullName + Path.DirectorySeparatorChar).AbsoluteUri, new Uri(b.FullName + Path.DirectorySeparatorChar).AbsoluteUri], CancellationToken.None);
            Assert.That(result.Contracts, Has.Count.EqualTo(2));
            Assert.That(result.Contracts.Single(value => value.Format == "datapackage").Tree!.Label, Is.EqualTo("Unsaved"));
            store.Close(new Uri(path));
            Assert.That((await service.InspectAsync(new Uri(path), CancellationToken.None)).Tree!.Label, Is.EqualTo("sales"));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    public async Task InvalidBuffers_ReturnSafeDiagnosticsWithoutReadingResourcesAsync(string newline)
    {
        var store = new DocumentStore();
        var uri = new Uri("file:///workspace/contract.yaml");
        store.Update(uri, "kind: DataContract\npassword: supersecret\nschema: [", 1);
        var result = await new ContractService(store).InspectAsync(uri, CancellationToken.None);
        Assert.That(result.Contract, Is.Null);
        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Message, Does.Not.Contain("supersecret"));
        Assert.That(result.Diagnostics[0].Range, Is.Not.Null);
        var text = Odcs.ReplaceLineEndings(newline);
        store.Update(uri, text.Replace("version: 1.0.0" + newline, ""), 2);
        var validation = await new ContractService(store).ValidateAsync(uri, CancellationToken.None);
        Assert.That(validation.Valid, Is.False);
        Assert.That(validation.Diagnostics.Select(value => value.Code), Does.Contain("ODCSV001"));
    }

    [Test]
    public async Task Inspection_RedactsSecretsFromCanonicalAndTreePayloadsAsync()
    {
        var store = new DocumentStore();
        var uri = new Uri("file:///workspace/secrets.yaml");
        store.Update(uri, Package + "\ncontributors:\n  - title: someone\n    email: 'password=supersecret'\n", 1);
        var result = await new ContractService(store).InspectAsync(uri, CancellationToken.None);
        Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(result), Does.Not.Contain("supersecret"));
        store.Update(uri, Odcs + "\ncustomProperties:\n  - property: password\n    value: supersecret\n", 2);
        result = await new ContractService(store).InspectAsync(uri, CancellationToken.None);
        Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(result), Does.Not.Contain("supersecret"));
    }

    [Test]
    public void Cancellation_IsObservedBeforeParsingOrDiscovery()
    {
        var service = new ContractService(new DocumentStore());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => service.Inspect(new(new Uri("file:///contract.yaml"), Package, 1), cancellation.Token));
        _ = Assert.ThrowsAsync<OperationCanceledException>(async () => await service.DiscoverAsync([new Uri(Path.GetTempPath()).AbsoluteUri], cancellation.Token));
    }

    [Test]
    public void JsonSourceLocations_UseUtf16ColumnsAfterEmoji()
    {
        const string text = """{"name":"😀","resources":[{"name":"rows","path":"missing.csv","schema":{"fields":[{"name":"id","type":"integer"}]}}]}""";
        var service = new ContractService(new DocumentStore());
        var result = service.Inspect(new(new Uri("file:///contract.json"), text, 1), CancellationToken.None);
        var field = Flatten(result.Tree!).Single(value => value.Kind == "field");
        var range = field.Location!.Range;
        var syntax = new SourceIndex(Packata.Documents.ContractDocument.Parse(text).Root).Find("/resources/0/schema/fields/0")!;
        Assert.That(text[range.Start.Character..range.End.Character], Does.StartWith("{\"name\":\"id\""), Newtonsoft.Json.JsonConvert.SerializeObject(new { range, syntax.Start, syntax.End, expected = text.IndexOf("{\"name\":\"id\"", StringComparison.Ordinal) }));
    }

    [Test]
    public async Task SharedParser_RetainsOriginalBaseForExplicitRelativeResourceReadsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "packata relative " + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "orders.csv"), "id,amount\n1,12\n");
            var parsed = Packata.Documents.ContractDocument.Parse(Package, baseUri: new Uri(root + Path.DirectorySeparatorChar));
            var native = (Packata.DataPackage.DataPackage)parsed.NativeDocument;
            await using var stream = await native.Resources[0].Paths[0].OpenAsync();
            using var reader = new StreamReader(stream);
            Assert.That(await reader.ReadToEndAsync(), Does.Contain("1,12"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void PartialEdits_KeepPreviouslyRecognizedCustomFilenamesVisible()
    {
        var service = new ContractService(new DocumentStore());
        var uri = new Uri("file:///workspace/custom-name.yaml");
        Assert.That(service.Inspect(new(uri, Odcs, 1), CancellationToken.None).Recognized, Is.True);
        var broken = service.Inspect(new(uri, "schema: [", 2), CancellationToken.None);
        Assert.That(broken.Recognized, Is.True);
        Assert.That(broken.Diagnostics.Select(value => value.Code), Does.Contain("PKT001"));
    }

    [Test]
    public void CyclicYaml_IsReportedWithoutRecursingThroughTheTree()
    {
        var service = new ContractService(new DocumentStore());
        var result = service.Inspect(new(new Uri("file:///contract.yaml"), "kind: DataContract\ncustomProperties: &cycle\n  - *cycle\n", 1), CancellationToken.None);
        Assert.That(result.Tree, Is.Null);
        Assert.That(result.Diagnostics.Select(value => value.Code), Does.Contain("PKT001"));
    }

    internal static IEnumerable<ContractNode> Flatten(ContractNode node)
        => new[] { node }.Concat(node.Children.SelectMany(Flatten));
}
