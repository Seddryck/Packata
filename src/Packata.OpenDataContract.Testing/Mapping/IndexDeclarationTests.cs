using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.OpenDataContract.Mapping;

namespace Packata.OpenDataContract.Testing.Mapping;

public class IndexDeclarationTests
{
    [Test]
    public void Maps_schema_level_indexes_from_yaml()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            apiVersion: v3.2.0
            kind: DataContract
            id: orders
            version: 1.0.0
            status: draft
            schema:
              - name: orders
                properties:
                  - name: Date
                    logicalType: date
                  - name: CustomerId
                    logicalType: string
                customProperties:
                  - vendor: packata
                    property: indexes
                    value:
                      version: 1
                      indexes:
                        - {property: Date, kind: ordered, order: ascending}
                        - {property: CustomerId, kind: bloom, comparison: ordinal}
                  - vendor: other
                    property: indexes
                    value: ignored
            """));

        var result = new DataContractFactory().LoadFromStream(stream).ToCanonicalContract();

        Assert.That(result.IsSuccessful, Is.True, string.Join(", ", result.Diagnostics));
        Assert.That(result.Value!.Assets.Single().Schema!.Indexes, Is.EqualTo(new DataIndex[]
        {
            new("Date", DataIndexKind.Ordered, Order: DataIndexOrder.Ascending),
            new("CustomerId", DataIndexKind.Bloom, Comparison: DataIndexComparison.Ordinal)
        }));
    }

    [Test]
    public void Rejects_a_declaration_inside_a_schema_property()
    {
        var schema = new SchemaObject
        {
            Name = "orders",
            Properties =
            [
                new SchemaProperty
                {
                    Name = "id",
                    CustomProperties = [new CustomProperty { Vendor = "packata", Property = "indexes" }]
                }
            ]
        };

        var result = new DataContract { Schema = [schema] }.ToCanonicalContract();

        Assert.That(result.IsSuccessful, Is.False);
        Assert.That(result.Diagnostics.Single(item => item.Code == "ODCS010").Message, Does.Contain("only on schema objects"));
    }

    [Test]
    public void Maps_json_metadata_with_explicit_order_and_comparison()
    {
        var result = ContractWithDeclaration("""
            {"version":1,"indexes":[{"property":"id","kind":"ordered","order":"descending","comparison":"ordinalIgnoreCase"}]}
            """).ToCanonicalContract();

        Assert.That(result.IsSuccessful, Is.True, string.Join(", ", result.Diagnostics));
        Assert.That(result.Value!.Assets.Single().Schema!.Indexes.Single(),
            Is.EqualTo(new DataIndex("id", DataIndexKind.Ordered, DataIndexOrder.Descending, DataIndexComparison.OrdinalIgnoreCase)));
    }

    [TestCase("[]", "mapping object")]
    [TestCase("""{"version":1}""", "requires an indexes sequence")]
    [TestCase("""{"version":1,"indexes":[{"property":1,"kind":"ordered"}]}""", "must be a string")]
    [TestCase("""{"version":1,"indexes":[],"indexes":[]}""", "duplicate index member")]
    [TestCase("""{"version":"1","indexes":[]}""", "integer version")]
    [TestCase("""{"version":2,"indexes":[]}""", "integer version")]
    [TestCase("""{"version":1,"indexes":{}}""", "sequence")]
    [TestCase("""{"version":1,"indexes":[{"property":"ID","kind":"ordered"}]}""", "top-level property")]
    [TestCase("""{"version":1,"indexes":[{"property":"id","kind":"hash"}]}""", "kind")]
    [TestCase("""{"version":1,"indexes":[{"property":"id","kind":"bloom","order":"ascending"}]}""", "order")]
    [TestCase("""{"version":1,"indexes":[{"property":"id","kind":"ordered","comparison":"other"}]}""", "comparison")]
    [TestCase("""{"version":1,"indexes":[{"property":"id","kind":"bloom"},{"property":"id","kind":"bloom"}]}""", "Duplicate")]
    public void Invalid_declaration_is_a_mapping_error(string json, string message)
    {
        var result = ContractWithDeclaration(json).ToCanonicalContract();

        Assert.That(result.IsSuccessful, Is.False);
        var diagnostic = result.Diagnostics.Single(item => item.Code == "ODCS010");
        Assert.That(diagnostic.SourcePath, Is.EqualTo("schema/orders/customProperties/0/value"));
        Assert.That(diagnostic.Message, Does.Contain(message));
    }

    private static DataContract ContractWithDeclaration(string json) => new()
    {
        Schema =
        [
            new SchemaObject
            {
                Name = "orders",
                Properties = [new SchemaProperty { Name = "id" }],
                CustomProperties = [new CustomProperty { Vendor = "packata", Property = "indexes", Value = JsonSerializer.Deserialize<JsonElement>(json) }]
            }
        ]
    };
}
