using System.Text.Json;
using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Ossie.Testing;

public class OssiePublicationTests
{
    [Test]
    public void MapsPublicationSelectionWithCompositeKeysAndOrderedRelationships()
    {
        var result = new OssieModelMapper().Map(CreateModel(), [new("ORDERS", "sales_api.order_details"), new("customers")]);
        var assets = result.RequireValue().Assets;
        var orders = assets[0];
        var schema = orders.Schema!;
        var relationship = schema.Relationships.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(assets.Select(asset => asset.Name), Is.EqualTo(new[] { "orders", "customers" }));
            Assert.That(orders.Description, Is.EqualTo("Published orders"));
            Assert.That(orders.PhysicalName, Is.EqualTo("sales_api.order_details"));
            Assert.That(assets[1].PhysicalName, Is.Null);
            Assert.That(schema.PrimaryKey, Is.EqualTo(new[] { "tenant_id", "id" }));
            Assert.That(assets[1].Schema!.UniqueKeys.Single(), Is.EqualTo(new[] { "tenant_id", "account_id" }));
            Assert.That(relationship.Kind, Is.EqualTo("foreignKey"));
            Assert.That(relationship.Fields, Is.EqualTo(new[] { "customer_id", "tenant_id" }));
            Assert.That(relationship.TargetAsset, Is.EqualTo("customers"));
            Assert.That(relationship.TargetFields, Is.EqualTo(new[] { "account_id", "tenant_id" }));
            Assert.That(result.Diagnostics.Select(item => item.SourcePath), Does.Contain("$/relationships/1"));
        }
    }

    [Test]
    public void ResolvesDirectAndCalculatedLineageAndReportsOpaqueExpressions()
    {
        var result = new OssieModelMapper().Map(CreateModel(), [new("orders")]);
        var asset = result.RequireValue().Assets.Single();
        var lineage = asset.Lineage.ToDictionary(item => item.TargetPath.Single());
        var direct = lineage["id"];
        var computed = lineage["net_amount"];
        var unresolved = lineage["opaque_amount"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(direct.IsIdentity, Is.True);
            Assert.That(direct.Sources.Single().Reference, Is.EqualTo("raw.orders"));
            Assert.That(direct.Sources.Single().Fields, Is.EqualTo(new[] { "Order ID" }));
            Assert.That(direct.Sources.Single().AssetId, Is.Null);
            Assert.That(computed.IsIdentity, Is.False);
            Assert.That(computed.Logic, Is.EqualTo("amount - discount + 1.25"));
            Assert.That(computed.Sources.Single().Fields, Is.EqualTo(new[] { "amount", "discount" }));
            Assert.That(unresolved.Logic, Is.EqualTo("ROUND(amount, 2)"));
            Assert.That(unresolved.Sources.Single().Fields, Is.Empty);
            Assert.That(result.Diagnostics.Select(item => item.SourcePath), Does.Contain("$/datasets/0/fields/4/expression"));
        }
    }

    [Test]
    public void PreservesUnknownTypeAndNullabilityInTheRowSchema()
    {
        var asset = new OssieModelMapper().Map(CreateModel(), [new("orders")]).RequireValue().Assets.Single();
        var fields = asset.Schema!.Fields.ToDictionary(field => field.Name);
        using var stream = new MemoryStream();
        asset.ToJsonSchema().Serialize(stream);
        stream.Position = 0;
        using var document = JsonDocument.Parse(stream);
        var properties = document.RootElement.GetProperty("properties");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fields["opaque_amount"].LogicalType, Is.Null);
            Assert.That(fields["net_amount"].LogicalType, Is.EqualTo("decimal"));
            Assert.That(fields.Values.All(field => !field.IsNullabilityKnown), Is.True);
            Assert.That(properties.GetProperty("opaque_amount").TryGetProperty("type", out _), Is.False);
            Assert.That(properties.GetProperty("net_amount").GetProperty("type").EnumerateArray().Select(node => node.GetString()),
                Is.EqualTo(new[] { "number", "null" }));
        }
    }

    [Test]
    public void ReportsInvalidPublicationSelections()
    {
        var result = new OssieModelMapper().Map(CreateModel(), [new("orders"), new("ORDERS"), new("missing")]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccessful, Is.False);
            Assert.That(result.Diagnostics.Where(item => item.Severity == MappingSeverity.Error).Select(item => item.SourcePath),
                Is.EqualTo(new[] { "$/publication/1/dataset", "$/publication/2/dataset" }));
        }
    }

    private static OssieModel CreateModel() => new()
    {
        Name = "retail",
        Datasets =
        [
            new()
            {
                Name = "orders",
                Source = "raw.orders",
                Description = "Published orders",
                PrimaryKey = ["TENANT_ID", "ID"],
                Fields =
                [
                    Field("tenant_id", "tenant", OssieDataType.Integer),
                    Field("id", "\"Order ID\"", OssieDataType.Integer),
                    Field("customer_id", "customer", OssieDataType.Integer),
                    Field("net_amount", "amount - discount + 1.25", OssieDataType.Decimal),
                    Field("opaque_amount", "ROUND(amount, 2)"),
                ],
            },
            new()
            {
                Name = "customers",
                Source = "raw.customers",
                UniqueKeys = [["tenant_id", "account_id"]],
                Fields = [Field("tenant_id", "tenant"), Field("account_id", "account")],
            },
            new()
            {
                Name = "audit",
                Source = "raw.audit",
                PrimaryKey = ["id"],
                Fields = [Field("id", "id")],
            },
        ],
        Relationships =
        [
            new()
            {
                Name = "order_customer",
                From = "ORDERS",
                To = "CUSTOMERS",
                FromColumns = ["CUSTOMER_ID", "TENANT_ID"],
                ToColumns = ["ACCOUNT_ID", "TENANT_ID"],
            },
            new()
            {
                Name = "order_audit",
                From = "orders",
                To = "audit",
                FromColumns = ["id"],
                ToColumns = ["id"],
            },
        ],
    };

    private static OssieField Field(string name, string sql, OssieDataType? type = null)
        => new() { Name = name, Expression = new() { Sql = sql }, DataType = type };
}
