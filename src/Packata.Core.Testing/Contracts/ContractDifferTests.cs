using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Core.Testing.Contracts;

public class ContractDifferTests
{
    private static DataContract Contract(params DataField[] fields) => new(
        new ContractIdentity("orders"), new ContractMetadata(),
        [new DataAsset("orders", "Orders", null, null, AssetKind.Table, new DataSchema(fields))],
        [], new ContractGovernance());

    private static DataContract WithAsset(DataContract contract, DataAsset asset) => contract with { Assets = [asset] };

    [Test]
    public void EquivalentGraphs_WithDifferentCollectionInstances_HaveNoChanges()
    {
        var before = Contract(new DataField("id", "integer", Constraints: [new("minimum", 0)]));
        var after = Contract(new DataField("id", "integer", Constraints: [new("minimum", 0)]));
        Assert.That(before.Diff(after).Changes, Is.Empty);
    }

    [Test]
    public void Changes_CanBeFilteredByOperationAssetAndConcreteType()
    {
        var before = Contract(new DataField("old", "string"), new DataField("amount", "integer"));
        var after = Contract(new DataField("amount", "number"), new DataField("new", "string"));
        var changes = before.Diff(after).Changes;
        Assert.Multiple(() =>
        {
            Assert.That(changes.OfType<IAdded>().Count(), Is.EqualTo(1));
            Assert.That(changes.OfType<IRemoved>().Count(), Is.EqualTo(1));
            Assert.That(changes.OfType<IModified>().Count(), Is.EqualTo(1));
            Assert.That(changes.OfType<IPropertyChange>().Count(), Is.EqualTo(3));
            Assert.That(changes.OfType<PropertyRemoved>().Single().Property.Name, Is.EqualTo("old"));
        });
        var type = changes.OfType<PropertyTypeChanged>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(type.Path.Value, Is.EqualTo("schema.orders.properties.amount.type"));
            Assert.That(type.DatasetId, Is.EqualTo("orders"));
            Assert.That(type.PropertyId, Is.EqualTo("amount"));
            Assert.That(type is { Previous: "integer", Current: "number" }, Is.True);
        });
    }

    [Test]
    public void ServersSlasAndDatasets_PreserveAddedAndRemovedValues()
    {
        var server = new DataEndpoint("production", null, EndpointKind.Database, null, new ConnectionLocation("postgres"));
        var sla = new ServiceLevel("freshness", Value: "1h");
        var populated = Contract() with { Endpoints = [server], Governance = new(ServiceLevels: [sla]) };
        var empty = populated with { Assets = [], Endpoints = [], Governance = new() };
        var additions = empty.Diff(populated).Changes;
        var removals = populated.Diff(empty).Changes;
        Assert.Multiple(() =>
        {
            Assert.That(additions.OfType<ServerAdded>().Single().Server.Id, Is.EqualTo(server.Id));
            Assert.That(additions.OfType<SlaAdded>().Single().Sla, Is.EqualTo(sla));
            Assert.That(additions.OfType<DatasetAdded>().Single().Dataset.Id, Is.EqualTo("orders"));
            Assert.That(removals.OfType<ServerRemoved>().Single().Server.Id, Is.EqualTo("production"));
            Assert.That(removals.OfType<SlaRemoved>().Single().Sla.Name, Is.EqualTo("freshness"));
            Assert.That(removals.OfType<DatasetRemoved>().Single().Path.Value, Is.EqualTo("schema.orders"));
            Assert.That(additions.Count, Is.EqualTo(3));
            Assert.That(removals.Count, Is.EqualTo(3));
        });
    }

    [Test]
    public void ServerAndSlaModifications_AreTypedAndRetainBothValues()
    {
        var before = Contract() with
        {
            Endpoints = [new("prod", null, EndpointKind.File, null, new PathLocation(["old.csv"]))],
            Governance = new(ServiceLevels: [new("freshness", Value: "1h")])
        };
        var after = before with
        {
            Endpoints = [before.Endpoints[0] with { Location = new PathLocation(["new.csv"]) }],
            Governance = new(ServiceLevels: [new("freshness", Value: "2h")])
        };
        var changes = before.Diff(after).Changes;
        Assert.Multiple(() =>
        {
            Assert.That(changes.OfType<ServerModified>().Single().Current.Location, Is.TypeOf<PathLocation>());
            Assert.That(changes.OfType<SlaModified>().Single().Previous.Value, Is.EqualTo("1h"));
            Assert.That(changes.OfType<SlaModified>().Single().Current.Value, Is.EqualTo("2h"));
        });
    }

    [Test]
    public void CollectionReordering_DoesNotChangeIdentityOrOrdering()
    {
        var before = Contract(new("z", "string"), new("a", "string"));
        var reversed = Contract(new("a", "string"), new("z", "string"));
        Assert.That(before.Diff(reversed).Changes, Is.Empty);
        var after = Contract(new("z", "integer"), new("a", "integer"));
        var paths = before.Diff(after).Changes.Select(change => change.Path.Value).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(paths, Is.Ordered.Using<string>(StringComparer.Ordinal));
            Assert.That(reversed.Diff(after).Changes.Select(change => change.Path.Value), Is.EqualTo(paths));
        });
    }

    [Test]
    public void NestedPropertiesAndArrayItems_UseSemanticPaths()
    {
        var before = Contract(new DataField("address", "object", Children: [new("city", "string")]),
            new DataField("lines", "array", Items: new("line", "integer")));
        var after = Contract(new DataField("address", "object", Children: [new("city", "integer")]),
            new DataField("lines", "array", Items: new("line", "number")));
        Assert.That(before.Diff(after).Changes.Select(change => change.Path.Value), Is.EqualTo(new[]
        {
            "schema.orders.properties.address.properties.city.type", "schema.orders.properties.lines.items.type"
        }));
    }

    [Test]
    public void Path_EscapesIdentifierSeparatorsWithoutCollisions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ContractPath.Root("schema").Append("a.b\\c").Value, Is.EqualTo(@"schema.a\.b\\c"));
            Assert.That(ContractPath.Root("a.b"), Is.Not.EqualTo(ContractPath.Root("a").Append("b")));
        });
    }

    [Test]
    public void PropertyMetadataAndConstraints_AreReportedWithPreviousAndCurrentValues()
    {
        var before = Contract(new DataField("amount", "number", Required: false, Constraints: [new("minimum", 0)]));
        var after = Contract(new DataField("amount", "number", Required: true, Constraints: [new("minimum", 1)]));
        var change = before.Diff(after).Changes.OfType<PropertyModified>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(change.Previous.Required, Is.False);
            Assert.That(change.Current.Required, Is.True);
            Assert.That(change.Current.Constraints[0].Value, Is.EqualTo(1));
        });
    }

    [Test]
    public void DatasetKindAndPrimaryKeyChanges_AreDetected()
    {
        var before = Contract(new DataField("id", "integer"));
        var after = WithAsset(before, before.Assets[0] with
        {
            Kind = AssetKind.View, Schema = before.Assets[0].Schema! with { PrimaryKey = ["id"] }
        });
        var change = before.Diff(after).Changes.OfType<DatasetModified>().Single();
        Assert.That(change.Current.Kind, Is.EqualTo(AssetKind.View));
        Assert.That(change.Current.Schema!.PrimaryKey, Is.EqualTo(new[] { "id" }));
    }

    [Test]
    public void RelationshipsAndQualityRules_AreComparedBySemanticIdentity()
    {
        var before = Contract(new DataField("id", "integer"));
        before = WithAsset(before, before.Assets[0] with
        {
            Schema = before.Assets[0].Schema! with { Relationships = [new(["id"], "customers", ["id"], "customer")] },
            QualityRules = [new("sql", "old description", "id > 0")]
        });
        var after = WithAsset(before, before.Assets[0] with
        {
            Schema = before.Assets[0].Schema! with { Relationships = [new(["id"], "clients", ["id"], "customer")] },
            QualityRules = [new("sql", "new description", "id > 0"), new("sql", Expression: "id < 100")]
        });
        var changes = before.Diff(after).Changes;
        Assert.Multiple(() =>
        {
            Assert.That(changes.OfType<RelationshipModified>().Single().Current.TargetAsset, Is.EqualTo("clients"));
            Assert.That(changes.OfType<QualityRuleModified>().Single().Current.Description, Is.EqualTo("new description"));
            Assert.That(changes.OfType<QualityRuleAdded>().Count(), Is.EqualTo(1));
            Assert.That(after.Diff(before).Changes.OfType<QualityRuleRemoved>().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void ContractMetadataGovernanceAndExtensions_AreIncluded()
    {
        var before = Contract();
        var after = before with
        {
            Identity = before.Identity with { Version = "2.0" },
            Metadata = new(Description: "Updated"),
            Governance = new(Terms: new(Usage: "Internal")),
            Extensions = ExtensionMetadata.For("example", new Dictionary<string, object?> { ["enabled"] = true })
        };
        Assert.That(before.Diff(after).Changes.OfType<ContractMetadataChanged>().Select(change => change.Path.Value),
            Is.EqualTo(new[] { "extensions", "governance", "identity", "metadata" }));
    }

    [Test]
    public void Results_AreReadOnlySnapshotsOfNestedInputCollections()
    {
        var allowed = new List<object?> { "one" };
        var fields = new List<DataField> { new("id", "string", Constraints: [new("enum", allowed)]) };
        var asset = new DataAsset("orders", "Orders", null, null, AssetKind.Table, new DataSchema(fields));
        var before = Contract() with { Assets = [] };
        var after = before with { Assets = [asset] };
        var result = before.Diff(after);
        fields.Clear();
        allowed.Add("two");
        var snapshot = result.Changes.OfType<DatasetAdded>().Single().Dataset.Schema!.Fields;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Count, Is.EqualTo(1));
            Assert.That((IEnumerable<object?>)snapshot[0].Constraints[0].Value!, Is.EqualTo(new[] { "one" }));
            Assert.Throws<NotSupportedException>(() => ((IList<IContractChange>)result.Changes).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<DataField>)snapshot).Clear());
        });
    }

    [Test]
    public void DuplicateIdentifiers_AreRejectedInsteadOfMatchedByPosition()
    {
        var duplicate = Contract(new DataField("id", "string"), new DataField("id", "integer"));
        Assert.Throws<ArgumentException>(() => Contract().Diff(duplicate));
        Assert.Throws<ArgumentException>(() => (Contract() with { Assets = [] }).Diff(duplicate));
    }

    [Test]
    public void OnlyEnumChanges_AreDetected()
    {
        var before = Contract();
        var after = WithAsset(before, before.Assets[0] with { Kind = AssetKind.View });
        Assert.That(before.Diff(after).Changes.Single(), Is.TypeOf<DatasetModified>());
    }

    [Test]
    public void SchemaAndArrayItems_AdditionsAndRemovals_AreDetected()
    {
        var withSchema = Contract(new DataField("lines", "array", Items: new("line", "string")));
        var withoutSchema = WithAsset(withSchema, withSchema.Assets[0] with { Schema = null });
        Assert.That(withoutSchema.Diff(withSchema).Changes.OfType<PropertyAdded>().Single().Property.Name, Is.EqualTo("lines"));
        Assert.That(withSchema.Diff(withoutSchema).Changes.OfType<PropertyRemoved>().Single().Property.Name, Is.EqualTo("lines"));
        var withoutItems = Contract(new DataField("lines", "array"));
        Assert.That(withoutItems.Diff(withSchema).Changes.OfType<PropertyAdded>().Single().Path.Value,
            Is.EqualTo("schema.orders.properties.lines.items"));
        Assert.That(withSchema.Diff(withoutItems).Changes.OfType<PropertyRemoved>().Single().Path.Value,
            Is.EqualTo("schema.orders.properties.lines.items"));
    }

    [Test]
    public void ExtensionDictionaries_AreOrderIndependentAndImmutable()
    {
        var values = new Dictionary<string, object?> { ["a"] = 1, ["b"] = new List<object?> { "x" } };
        var before = Contract() with { Extensions = ExtensionMetadata.For("test", values) };
        var after = Contract() with
        {
            Extensions = ExtensionMetadata.For("test", new Dictionary<string, object?>
            {
                ["b"] = new[] { "x" }, ["a"] = 1
            })
        };
        Assert.That(before.Diff(after).Changes, Is.Empty);
        var change = (Contract().Diff(before).Changes.Single() as ContractMetadataChanged)!;
        var snapshot = ((ExtensionMetadata)change.Current!)["test"];
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)snapshot).Clear());
        Assert.That(before.Extensions["test"].Count, Is.EqualTo(2));
    }

    [Test]
    public void UnsupportedMutableValues_AreRejected()
    {
        var before = Contract() with { Governance = new(ServiceLevels: [new("freshness", Value: new System.Text.StringBuilder())]) };
        Assert.Throws<NotSupportedException>(() => before.Diff(Contract()));
    }

    [Test]
    public void YamlStyleDictionaries_AreSnapshottedWithTheirStringKeysAndNestedValues()
    {
        var values = new Dictionary<object, object> { ["nested"] = new Dictionary<object, object> { ["value"] = "retained" } };
        var before = Contract();
        var after = before with { Extensions = ExtensionMetadata.For("yaml", new Dictionary<string, object?> { ["settings"] = values }) };
        var change = (ContractMetadataChanged)before.Diff(after).Changes.Single();
        var snapshot = (ExtensionMetadata)change.Current!;
        var settings = (IReadOnlyDictionary<string, object?>)snapshot["yaml"]["settings"]!;
        Assert.That(((IReadOnlyDictionary<string, object?>)settings["nested"]!)["value"], Is.EqualTo("retained"));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)settings).Clear());
    }

    [Test]
    public void JsonExtensionValues_AreSnapshottedBeforeTheirDocumentIsDisposed()
    {
        ContractDiff diff;
        using (var document = System.Text.Json.JsonDocument.Parse("{\"count\":1,\"values\":[true,null,1e100]}"))
        {
            var contract = Contract() with
            {
                Extensions = ExtensionMetadata.For("test", new Dictionary<string, object?> { ["json"] = document.RootElement })
            };
            diff = Contract().Diff(contract);
        }
        var extension = (ExtensionMetadata)((ContractMetadataChanged)diff.Changes.Single()).Current!;
        var json = (IReadOnlyDictionary<string, object?>)extension["test"]["json"]!;
        Assert.Multiple(() =>
        {
            Assert.That(json["count"], Is.EqualTo(1L));
            Assert.That((IEnumerable<object?>)json["values"]!, Is.EqualTo(new object?[] { true, null, 1e100 }));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)json).Clear());
        });
    }

    [Test]
    public void NullAndEmptyRuleExpressions_HaveDistinctSemanticIdentities()
    {
        var before = Contract();
        var after = WithAsset(before, before.Assets[0] with { QualityRules = [new("sql"), new("sql", Expression: "")] });
        var changes = before.Diff(after).Changes.OfType<QualityRuleAdded>().ToArray();
        Assert.That(changes.Length, Is.EqualTo(2));
        Assert.That(changes.Select(change => change.Path).Distinct().Count(), Is.EqualTo(2));
    }

    [Test]
    public void NullContracts_AreRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => ContractDiffer.Diff(null!, Contract()));
            Assert.Throws<ArgumentNullException>(() => Contract().Diff(null!));
        });
    }
}
