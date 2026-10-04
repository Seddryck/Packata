using NUnit.Framework;
using Packata.Core.Storage;
using Packata.DataPackage.Validation;

namespace Packata.DataPackage.Testing.Validation;

public class DataPackageValidatorTests
{
    private readonly PathFactory _paths = new(new LocalDirectoryDocumentContainer(), new StorageProvider());

    [Test]
    public void Validate_ValidPackage_ReturnsNoIssues()
    {
        var package = new DataPackage
        {
            Name = "population",
            Resources =
            [
                new Resource
                {
                    Name = "countries", Type = "table", Paths = [_paths.Create("countries.csv")],
                    Schema = new Schema
                    {
                        Fields = [new StringField { Name = "code", Type = "string" }],
                        PrimaryKey = ["code"]
                    }
                },
                new Resource
                {
                    Name = "population", Type = "table", Paths = [_paths.Create("population.csv")],
                    Schema = new Schema
                    {
                        Fields =
                        [
                            new StringField { Name = "country", Type = "string" },
                            new IntegerField { Name = "count", Type = "integer" }
                        ],
                        ForeignKeys =
                        [
                            new ForeignKey
                            {
                                Fields = ["country"],
                                Reference = new Reference { Resource = "countries", Fields = ["code"] }
                            }
                        ]
                    }
                }
            ]
        };

        var result = DataPackageValidator.Validate(package);

        Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, result.Issues));
    }

    [Test]
    public void Validate_InvalidPackage_ReturnsPathBasedIssues()
    {
        var package = new DataPackage
        {
            Resources =
            [
                new Resource { Name = "duplicate" },
                new Resource
                {
                    Name = "duplicate", Type = "table",
                    Schema = new Schema
                    {
                        Fields = [new Field { Name = "id", Type = "unsupported" }],
                        PrimaryKey = ["missing"]
                    },
                    Dialect = new TableDialect { Header = false, HeaderRows = [1], ItemType = "scalar" }
                }
            ]
        };

        var result = DataPackageValidator.Validate(package);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Issues.Select(issue => issue.Path), Does.Contain("$.resources[1].name"));
        Assert.That(result.Issues.Select(issue => issue.Path), Does.Contain("$.resources[1]"));
        Assert.That(result.Issues.Select(issue => issue.Path), Does.Contain("$.resources[1].schema.fields[0].type"));
        Assert.That(result.Issues.Select(issue => issue.Path), Does.Contain("$.resources[1].schema.primaryKey"));
        Assert.That(result.Issues.Select(issue => issue.Path), Does.Contain("$.resources[1].dialect.itemType"));
    }

    [Test]
    public void Validate_NonTabularResourceWithoutLocation_IsValid()
    {
        var package = new DataPackage { Resources = [new Resource { Name = "metadata" }] };

        var result = DataPackageValidator.Validate(package);

        Assert.That(result.IsValid, Is.True);
    }
}
