using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Ossie.Testing;

public class OssieModelMapperTests
{
    [Test]
    public void MapsCalculatedFieldAndMeasuresToTheirOwningAsset()
    {
        var result = new OssieModelMapper().Map(CreateModel());
        var assets = result.RequireValue().Assets;
        var orders = assets.Single(asset => asset.Name == "orders");
        var schema = orders.Schema!;
        var revenue = schema.Measures[0];
        using var stream = new MemoryStream();
        orders.ToJsonSchema().Serialize(stream);
        stream.Position = 0;
        using var rowSchema = System.Text.Json.JsonDocument.Parse(stream);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(schema.Fields.Select(field => field.Name), Is.EqualTo(new[] { "amount", "discount", "net_amount" }));
            Assert.That(orders.Lineage.Single(item => item.TargetPath.Single() == "net_amount").Logic, Is.EqualTo("amount - discount"));
            Assert.That(revenue.Name, Is.EqualTo("total_revenue"));
            Assert.That(revenue.Description, Is.EqualTo("Revenue after discounts"));
            Assert.That(revenue.ResultType, Is.EqualTo("decimal"));
            Assert.That(revenue.Expressions, Is.EqualTo(new DataMeasureExpression[]
            {
                new("SUM(net_amount)"),
                new("SUM(CAST(net_amount AS MONEY))", "warehouse/v2"),
            }));
            Assert.That(assets.Single(asset => asset.Name == "returns").Schema!.Measures, Is.Empty);
            Assert.That(result.Diagnostics.Single().SourcePath, Is.EqualTo("$/metrics/0"));
            Assert.That(rowSchema.RootElement.GetProperty("properties").EnumerateObject().Select(property => property.Name),
                Is.EqualTo(new[] { "amount", "discount", "net_amount" }));
        }
    }

    [Test]
    public void MissingTypeAndDialectStayUnknown()
    {
        var measure = new OssieModelMapper().Map(CreateModel()).RequireValue().Assets[0].Schema!.Measures[1];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(measure.ResultType, Is.Null);
            Assert.That(measure.Expressions.Single(), Is.EqualTo(new DataMeasureExpression("COUNT(*)")));
            Assert.That(new DataSchema([]).Measures, Is.Empty);
        }
    }

    [Test]
    public void DiagnosesMetricNameConflictingWithRowField()
    {
        var model = CreateModel();
        var orders = model.Datasets[0] with { Metrics = [new() { Name = "NET_AMOUNT", Expression = new() { Sql = "COUNT(*)" } }] };
        var result = new OssieModelMapper().Map(model with { Datasets = [orders] });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccessful, Is.False);
            Assert.That(result.Diagnostics.Single().SourcePath, Is.EqualTo("$/datasets/0/metrics/0/name"));
        }
    }

    private static OssieModel CreateModel() => new()
    {
        Name = "sales",
        Datasets =
        [
            new()
            {
                Name = "orders",
                Source = "sales.orders",
                Fields =
                [
                    new() { Name = "amount", DataType = OssieDataType.Decimal, Expression = new() { Sql = "amount" } },
                    new() { Name = "discount", DataType = OssieDataType.Decimal, Expression = new() { Sql = "discount" } },
                    new() { Name = "net_amount", DataType = OssieDataType.Decimal, Expression = new() { Sql = "amount - discount" } },
                ],
                Metrics =
                [
                    new()
                    {
                        Name = "total_revenue",
                        Description = "Revenue after discounts",
                        DataType = OssieDataType.Decimal,
                        Expression = new()
                        {
                            Sql = "SUM(net_amount)",
                            Dialects = [new() { Dialect = "warehouse/v2", Expression = "SUM(CAST(net_amount AS MONEY))" }],
                        },
                    },
                    new() { Name = "count_orders", Expression = new() { Sql = "COUNT(*)" } },
                ],
            },
            new() { Name = "returns", Source = "sales.returns" },
        ],
        Metrics = [new() { Name = "global_revenue", Expression = new() { Sql = "AGG(orders.total_revenue)" } }],
    };
}
