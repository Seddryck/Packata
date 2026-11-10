using NUnit.Framework;
using Packata.OpenDataContract.Mapping;
using Packata.OpenLineage;

namespace Packata.OpenDataContract.Testing.Mapping;

public sealed class LineageTests
{
    [Test]
    public void Arithmetic_derivation_exports_both_physical_source_columns()
    {
        var mapped = new OpenDataContractMapper().Map(Contract("quantity * unit_price"));
        Assert.That(mapped.Diagnostics, Is.Empty);
        var target = mapped.RequireValue().Assets.Single(asset => asset.Id == "prices");
        var lineage = target.Lineage.Single();
        Assert.That(lineage.Logic, Is.EqualTo("quantity * unit_price"));
        Assert.That(lineage.Sources.Single().AssetId, Is.EqualTo("order_lines"));
        Assert.That(lineage.Sources.Single().Fields, Is.EqualTo(new[] { "qty", "unit_price" }));

        var result = OpenLineageExporter.Export(target, Options);
        Assert.That(result.IsSuccessful, Is.True);
        var json = result.RequireValue();
        Assert.That(json["run"], Is.Null);
        Assert.That(json["eventType"], Is.Null);
        Assert.That(json["inputs"]![0]!["name"]!.GetValue<string>(), Is.EqualTo("commerce.raw.order_lines"));
        var fields = json["outputs"]![0]!["facets"]!["columnLineage"]!["fields"]!["total_price"]!["inputFields"]!.AsArray();
        Assert.That(fields.Select(field => field!["field"]!.GetValue<string>()), Is.EqualTo(new[] { "qty", "unit_price" }));
        foreach (var field in fields)
        {
            var transformation = field!["transformations"]![0]!;
            Assert.That(transformation["type"]!.GetValue<string>(), Is.EqualTo("DIRECT"));
            Assert.That(transformation["subtype"]!.GetValue<string>(), Is.EqualTo("TRANSFORMATION"));
            Assert.That(transformation["description"]!.GetValue<string>(), Is.EqualTo("Multiply quantity by unit price."));
        }
    }

    [TestCase("quantity * missing_field")]
    [TestCase("SELECT quantity FROM order_lines")]
    public void Unresolved_expression_preserves_dataset_lineage_without_guessing_columns(string expression)
    {
        var mapped = new OpenDataContractMapper().Map(Contract(expression));
        Assert.That(mapped.Diagnostics.Select(diagnostic => diagnostic.Code), Is.EqualTo(new[] { "ODCS009" }));
        var target = mapped.RequireValue().Assets.Single(asset => asset.Id == "prices");
        Assert.That(target.Lineage.Single().Logic, Is.EqualTo(expression));
        Assert.That(target.Lineage.Single().Sources.Single().Fields, Is.Empty);

        var json = OpenLineageExporter.Export(target, Options).RequireValue();
        Assert.That(json["inputs"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(json["outputs"]![0]!["facets"], Is.Null);
    }

    [Test]
    public void Ambiguous_columns_are_not_inferred()
    {
        var contract = Contract("quantity * unit_price");
        contract.Schema.Add(new SchemaObject { Name = "archived_orders", Properties = [new() { Name = "quantity" }] });
        contract.Schema[1].Properties[0].TransformSourceObjects!.Add("archived_orders");
        var mapped = new OpenDataContractMapper().Map(contract);
        Assert.That(mapped.Diagnostics.Select(diagnostic => diagnostic.Code), Is.EqualTo(new[] { "ODCS009" }));
        Assert.That(mapped.RequireValue().Assets[1].Lineage.Single().Sources.SelectMany(source => source.Fields), Is.Empty);
    }

    private static OpenLineageExportOptions Options => new(
        new("commerce-prod", "build_prices"),
        new("postgres://warehouse.example.com:5432", "commerce.analytics.prices"),
        new Uri("https://example.com/odcs-exporter"),
        DateTimeOffset.UnixEpoch,
        source => source.Reference == "order_lines"
            ? new("postgres://warehouse.example.com:5432", "commerce.raw.order_lines")
            : null);

    private static DataContract Contract(string logic) => new()
    {
        Id = "commerce",
        Schema =
        [
            new()
            {
                Name = "order_lines",
                Properties = [new() { Name = "quantity", PhysicalName = "qty" }, new() { Name = "unit_price" }],
            },
            new()
            {
                Name = "prices",
                Properties =
                [
                    new()
                    {
                        Name = "total_price",
                        TransformSourceObjects = ["order_lines"],
                        TransformLogic = logic,
                        TransformDescription = "Multiply quantity by unit price.",
                    },
                ],
            },
        ],
    };
}
