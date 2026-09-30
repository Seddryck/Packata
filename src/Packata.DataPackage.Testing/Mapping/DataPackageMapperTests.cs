using Packata.DataPackage;
using Packata.Core.Storage;
using Packata.DataPackage.Mapping;
using NUnit.Framework;

namespace Packata.DataPackage.Testing.Mapping;

public class DataPackageMapperTests
{
    [Test]
    public void Map_preserves_schema_relationships_and_dialect()
    {
        var package = new DataPackage
        {
            Name = "sales", Title = "Sales package",
            Resources =
            [
                new Resource { Name = "customers", Paths = [new StubPath("customers.csv")] },
                new Resource
                {
                    Name = "orders", Format = "csv", Paths = [new StubPath("orders.csv")],
                    Dialect = new TableDelimitedDialect { Delimiter = ';' },
                    Schema = new Schema
                    {
                        PrimaryKey = ["id"],
                        Fields =
                        [
                            new IntegerField { Name = "id", Type = "integer",
                                Constraints = Constraints(new RequiredConstraint(true), new MinimumConstraint(1)) },
                            new IntegerField { Name = "customer_id", Type = "integer" }
                        ],
                        ForeignKeys = [new ForeignKey { Fields = ["customer_id"],
                            Reference = new Reference { Resource = "customers", Fields = ["id"] } }]
                    }
                }
            ]
        };

        var result = new DataPackageMapper().Map(package);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.Value!.Assets, Has.Count.EqualTo(2));
            Assert.That(result.Value.Assets[1].Schema!.Fields[0].Required, Is.True);
            Assert.That(result.Value.Assets[1].Schema!.Fields[0].Constraints.Single().Kind, Is.EqualTo("minimum"));
            Assert.That(result.Value.Assets[1].Schema!.Relationships.Single().TargetAsset, Is.EqualTo("customers"));
            Assert.That(result.Value.Endpoints[1].Format!.Options["delimiter"], Is.EqualTo(';'));
        });
    }

    [Test]
    public void Map_reports_generated_names_and_absent_locations()
    {
        var result = new DataPackageMapper().Map(new DataPackage { Resources = [new Resource()] });
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Identity.Id, Is.EqualTo("data-package"));
            Assert.That(result.Value.Assets.Single().Id, Is.EqualTo("resource-1"));
            Assert.That(result.Value.Endpoints, Is.Empty);
            Assert.That(result.Diagnostics.Select(x => x.Code),
                Is.EquivalentTo(new[] { "DP001", "DP002", "DP003" }));
        });
    }

    private static FieldConstraintCollection Constraints(params Constraint[] values)
    {
        var collection = new FieldConstraintCollection(); collection.AddRange(values); return collection;
    }

    private sealed class StubPath(string value) : IPath
    {
        public string Value { get; } = value;
        public bool IsFullyQualified => false;
        public Task<Stream> OpenAsync() => throw new NotSupportedException();
        public Task<bool> ExistsAsync() => Task.FromResult(true);
    }
}
