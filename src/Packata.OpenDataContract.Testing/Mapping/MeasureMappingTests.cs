using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.OpenDataContract.Mapping;
using Packata.OpenDataContract.Serialization.Yaml;

namespace Packata.OpenDataContract.Testing.Mapping;

public class MeasureMappingTests
{
    [Test]
    public void SerializesMeasuresAndReadsThemOutsideTheRowSchema()
    {
        var schema = new DataSchema([new("net_amount", "number")])
        {
            Measures =
            [
                new("total_revenue", "Revenue after discounts", "decimal")
                {
                    Expressions = [new("SUM(net_amount)", "warehouse/v2")],
                },
                new("count_orders") { Expressions = [new("COUNT(*)")] },
            ],
        };
        var asset = new DataAsset("orders", "orders", null, null, AssetKind.View, schema);
        var measures = asset.ToOdcsMeasureProperties().RequireValue();
        var native = new DataContract
        {
            Id = "sales",
            Version = "1.0.0",
            Status = "draft",
            Schema = [new() { Name = "orders", Properties = [new() { Name = "net_amount" }, .. measures] }],
        };
        var yaml = new DataContractSerializer().Serialize(native);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
        var reloaded = new DataContractFactory().LoadFromStream(stream).ToCanonicalContract().RequireValue();
        var reloadedSchema = reloaded.Assets.Single().Schema!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(yaml, Does.Contain("semanticType: measure"));
            Assert.That(yaml, Does.Contain("transformLogic: SUM(net_amount)"));
            Assert.That(reloadedSchema.Fields.Select(field => field.Name), Is.EqualTo(new[] { "net_amount" }));
            Assert.That(reloadedSchema.Measures[0].Description, Is.EqualTo("Revenue after discounts"));
            Assert.That(reloadedSchema.Measures[0].ResultType, Is.EqualTo("number"));
            Assert.That(reloadedSchema.Measures[0].Expressions, Is.EqualTo(schema.Measures[0].Expressions));
            Assert.That(reloadedSchema.Measures[1].ResultType, Is.Null);
            Assert.That(reloadedSchema.Measures[1].Expressions, Is.EqualTo(schema.Measures[1].Expressions));
            Assert.That(reloaded.Assets.Single().Lineage, Is.Empty);
        }
    }

    [Test]
    public void ReportsDialectAlternativesThatDoNotFitInTransformLogic()
    {
        var asset = new DataAsset("orders", "orders", null, null, AssetKind.View, new DataSchema([])
        {
            Measures =
            [
                new("total_revenue")
                {
                    Expressions = [new("SUM(net_amount)", "warehouse"), new("SUM(amount - discount)", "other")],
                },
            ],
        });
        var result = asset.ToOdcsMeasureProperties();
        var property = result.RequireValue().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(property.SemanticType, Is.EqualTo("measure"));
            Assert.That(property.TransformLogic, Is.Null);
            Assert.That(result.Diagnostics.Single().SourcePath, Is.EqualTo("schema/orders/measures/0/expressions"));
        }
    }
}
