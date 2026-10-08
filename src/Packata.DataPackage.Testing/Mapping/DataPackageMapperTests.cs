using Packata.DataPackage;
using Packata.Core.Storage;
using Packata.DataPackage.Mapping;
using NUnit.Framework;

namespace Packata.DataPackage.Testing.Mapping;

public class DataPackageMapperTests
{
    [Test]
    public void ToCanonicalContract_ReturnsCanonicalContract()
    {
        var package = new DataPackage { Name = "sales" };

        var contract = package.ToCanonicalContract().RequireValue();

        Assert.That(contract.Identity.Id, Is.EqualTo("sales"));
    }

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
                    Dialect = new TableDelimitedDialect { Delimiter = ";" },
                    Schema = new Schema
                    {
                        PrimaryKey = ["id"],
                        Fields =
                        [
                            new IntegerField { Name = "id", Type = "integer",
                                PhysicalName = "order_id",
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
            Assert.That(result.Value.Assets[1].Schema!.Fields[0].PhysicalName, Is.EqualTo("order_id"));
            Assert.That(result.Value.Assets[1].Schema!.Fields[0].Constraints.Single().Kind, Is.EqualTo("minimum"));
            Assert.That(result.Value.Assets[1].Schema!.Relationships.Single().TargetAsset, Is.EqualTo("customers"));
            Assert.That(result.Value.Endpoints[1].Format!.Options["delimiter"], Is.EqualTo(";"));
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

    [Test]
    public void Map_preserves_remaining_v2_metadata()
    {
        var package = new DataPackage
        {
            Name = "sample",
            Sources = [new Source { Title = "Registry", Path = "https://example.com/source", Version = "2" }],
            Resources =
            [
                new Resource
                {
                    Name = "data", Homepage = "https://example.com/data",
                    Licenses = [new License { Name = "CC0-1.0" }],
                    Data = Array.Empty<object>(),
                    Schema = new Schema
                    {
                        Fields =
                        [
                            new BooleanField
                            {
                                Name = "flag", Type = "boolean", Example = "yes",
                                TrueValues = ["yes"], FalseValues = ["no"],
                                Categories = [new CategoryLabel("yes"), new CategoryLabel("no")]
                            }
                        ]
                    }
                }
            ]
        };

        var contract = new DataPackageMapper().Map(package).RequireValue();
        var fieldExtensions = contract.Assets.Single().Schema!.Fields.Single().Extensions["datapackage"];
        var resourceExtensions = contract.Assets.Single().Extensions["datapackage"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(contract.Governance!.References.Select(reference => reference.Url),
                Does.Contain("https://example.com/source"));
            Assert.That(contract.Governance.References.Select(reference => reference.Url),
                Does.Contain("https://example.com/data"));
            Assert.That(fieldExtensions["example"], Is.EqualTo("yes"));
            Assert.That(fieldExtensions["trueValues"], Is.EqualTo(new[] { "yes" }));
            Assert.That(contract.Assets.Single().Schema!.Fields.Single().Constraints.Single().Kind,
                Is.EqualTo("enum"));
            Assert.That(resourceExtensions["licenses"], Is.EqualTo(package.Resources[0].Licenses));
        }
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
