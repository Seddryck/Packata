using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Cli.Testing;

[TestFixture]
public sealed class DiffPresenterTests
{
    private static IEnumerable<TestCaseData> Changes()
    {
        var path = ContractPath.Root("example");
        var server = new DataEndpoint("prod", null, EndpointKind.File, null, new PathLocation(["data.csv"]));
        var sla = new ServiceLevel("freshness", Value: null);
        var dataset = new DataAsset("orders", "Orders", null, null, AssetKind.Table, new DataSchema([]));
        var field = new DataField("amount", null);
        var relationship = new DataRelationship(["id"], "customers", ["id"], "customer");
        var rule = new DataQualityRule("sql", Expression: "amount > 0");
        yield return Case(new ServerAdded(path, server), "added", "server", "server");
        yield return Case(new ServerRemoved(path, server), "removed", "server", "server");
        yield return Case(new ServerModified(path, server, server), "modified", "server", "previous", "current");
        yield return Case(new SlaAdded(path, sla), "added", "sla", "sla");
        yield return Case(new SlaRemoved(path, sla), "removed", "sla", "sla");
        yield return Case(new SlaModified(path, sla, sla), "modified", "sla", "previous", "current");
        yield return Case(new DatasetAdded(path, dataset), "added", "dataset", "dataset");
        yield return Case(new DatasetRemoved(path, dataset), "removed", "dataset", "dataset");
        yield return Case(new DatasetModified(path, dataset, dataset), "modified", "dataset", "previous", "current");
        yield return Case(new PropertyAdded(path, "orders", "amount", field), "added", "property", "datasetId", "propertyId", "property");
        yield return Case(new PropertyRemoved(path, "orders", "amount", field), "removed", "property", "datasetId", "propertyId", "property");
        yield return Case(new PropertyModified(path, "orders", "amount", field, field), "modified", "property", "datasetId", "propertyId", "previous", "current");
        yield return Case(new PropertyTypeChanged(path, "orders", "amount", null, "number"), "modified", "property", "datasetId", "propertyId", "previous", "current");
        yield return Case(new RelationshipAdded(path, relationship), "added", "relationship", "relationship");
        yield return Case(new RelationshipRemoved(path, relationship), "removed", "relationship", "relationship");
        yield return Case(new RelationshipModified(path, relationship, relationship), "modified", "relationship", "previous", "current");
        yield return Case(new QualityRuleAdded(path, rule), "added", "qualityRule", "qualityRule");
        yield return Case(new QualityRuleRemoved(path, rule), "removed", "qualityRule", "qualityRule");
        yield return Case(new QualityRuleModified(path, rule, rule), "modified", "qualityRule", "previous", "current");
        yield return Case(new ContractMetadataChanged(path, null, new ContractMetadata(Description: "Current")), "modified", "contractMetadata", "previous", "current");
    }

    private static TestCaseData Case(IContractChange change, string operation, string asset, params string[] payload)
        => new TestCaseData(change, operation, asset, payload).SetName($"Projection_{change.GetType().Name}");

    [TestCaseSource(nameof(Changes))]
    public void EverySupportedKind_HasExplicitClassificationsAndPayload(IContractChange change, string operation, string asset, string[] payload)
    {
        var json = DiffPresenter.Change(change);
        Assert.Multiple(() =>
        {
            Assert.That((string?)json["kind"], Is.EqualTo(change.GetType().Name));
            Assert.That((string?)json["operation"], Is.EqualTo(operation));
            Assert.That((string?)json["asset"], Is.EqualTo(asset));
            Assert.That((string?)json["path"], Is.EqualTo(change.Path.Value));
            Assert.That(json.Properties().Skip(4).Select(x => x.Name), Is.EqualTo(payload));
            Assert.That(json.ToString(), Does.Not.Contain("$type").And.Not.Contain("Packata.Core"));
        });
    }

    [Test]
    public void NullValues_ArePresentWhileInapplicablePayloadsAreAbsent()
    {
        var json = DiffPresenter.Change(new PropertyTypeChanged(ContractPath.Root("property"), "orders", "amount", null, "number"));
        Assert.That(json.Property("previous"), Is.Not.Null);
        Assert.That(json["previous"]!.Type, Is.EqualTo(JTokenType.Null));
        Assert.That(json.Property("property"), Is.Null);
    }

    [Test]
    public void PayloadsAndSourcesAndDiagnostics_AreRecursivelyRedactedWithoutLosingExtensionValues()
    {
        var extension = ExtensionMetadata.For("VendorCase", new Dictionary<string, object?>
        {
            ["PreserveCase"] = "retained",
            ["nested"] = new Dictionary<string, object?> { ["password"] = "DO-NOT-EMIT", ["note"] = "token=DO-NOT-EMIT" }
        });
        var server = new DataEndpoint("prod", "pwd=DO-NOT-EMIT", EndpointKind.Database, null,
            new ConnectionLocation("postgres", ConnectionUrl: "postgres://alice:DO-NOT-EMIT@host/db"), Extensions: extension);
        var change = DiffPresenter.Change(new ServerAdded(ContractPath.Root("servers").Append("prod"), server));
        Assert.That((string?)change["server"]!["extensions"]!["VendorCase"]!["PreserveCase"], Is.EqualTo("retained"));
        Assert.That(change.ToString(), Does.Not.Contain("DO-NOT-EMIT"));
        var empty = new DataContract(new("id"), new(), [], [], new());
        var result = DiffPresenter.Result("https://user:DO-NOT-EMIT@host/old", "https://host/new?token=DO-NOT-EMIT",
            empty.Diff(empty), [new("candidate", "pwd=DO-NOT-EMIT", "WARN", MappingSeverity.Warning, "$", "secret=DO-NOT-EMIT")]);
        Assert.That(result.ToString(), Does.Not.Contain("DO-NOT-EMIT"));
    }

    [Test]
    public void UnknownKindsAndClassifications_AreRejectedExplicitly()
    {
        Assert.Throws<ContractOperationException>(() => DiffPresenter.Change(new UnknownChange()));
        Assert.Throws<ContractOperationException>(() => DiffPresenter.Change(new UnclassifiedChange()));
    }

    private sealed record UnknownChange : IContractChange, IModified, IServerChange
    {
        public ContractPath Path { get; } = ContractPath.Root("unknown");
    }

    private sealed record UnclassifiedChange : IContractChange
    {
        public ContractPath Path { get; } = ContractPath.Root("unknown");
    }
}
