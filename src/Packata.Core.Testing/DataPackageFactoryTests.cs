using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Packata.Core.Serialization.Json;

namespace Packata.Core.Testing;

public class DataPackageFactoryTests
{
    [Test]
    public void LoadFromStream_WithValidStream_ReturnsDataPackage()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"{
            ""name"": ""my-data-package"",
            ""title"": ""My Data Package"",
            ""description"": ""A really long description"",
            ""keywords"": [""data"", ""example""],
            ""Homepage"": ""https://github.com/package"",  
            ""resources"": [
                {
                    ""name"": ""data.csv"",
                    ""path"": ""https://example.com/data.csv"",
                    ""format"": ""csv""
                }
            ]
        }"));
        var factory = new DataPackageFactory();
        var dataPackage = factory.LoadFromStream(stream);
        Assert.That(dataPackage, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataPackage.Name, Is.EqualTo("my-data-package"));
            Assert.That(dataPackage.Title, Is.EqualTo("My Data Package"));
            Assert.That(dataPackage.Description, Is.EqualTo("A really long description"));
            Assert.That(dataPackage.Homepage, Is.EqualTo("https://github.com/package"));
            Assert.That(dataPackage.Keywords, Does.Contain("data"));
            Assert.That(dataPackage.Keywords, Does.Contain("example"));
            Assert.That(dataPackage.Resources, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void LoadFromStream_WithValidStream_ReturnsContributors()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"{
            ""name"": ""my-data-package"",
            ""contributors"": [
                {
                    ""title"": ""Jane Doe"",
                    ""givenName"": ""Jane"",
                    ""familyName"": ""Doe"",
                    ""organization"": ""The Company"",
                    ""email"": ""jane.doe@company.com"",
                    ""path"": ""jane-doe.html"",
                    ""roles"": [""creator""]
                }, 
                {
                    ""title"": ""John Doe"",
                    ""email"": ""john.doe@company.com""
                }
            ]
        }"));
        var factory = new DataPackageFactory();
        var dataPackage = factory.LoadFromStream(stream);
        Assert.That(dataPackage, Is.Not.Null);
        Assert.That(dataPackage.Contributors, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(dataPackage.Contributors[0].Title, Is.EqualTo("Jane Doe"));
            Assert.That(dataPackage.Contributors[0].GivenName, Is.EqualTo("Jane"));
            Assert.That(dataPackage.Contributors[0].FamilyName, Is.EqualTo("Doe"));
            Assert.That(dataPackage.Contributors[0].Path, Is.EqualTo("jane-doe.html"));
            Assert.That(dataPackage.Contributors[0].Organization, Is.EqualTo("The Company"));
            Assert.That(dataPackage.Contributors[0].Email, Is.EqualTo("jane.doe@company.com"));
            Assert.That(dataPackage.Contributors[0].Roles, Has.Count.EqualTo(1));
            Assert.That(dataPackage.Contributors[0].Roles, Does.Contain("creator"));
        });
    }

    [Test]
    public void LoadFromStream_WithValidStream_ReturnsResourceSources()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"{
            ""name"": ""my-data-package"",
            ""resources"": [
                {
                    ""name"": ""data.csv"",
                    ""path"": ""https://example.com/data.csv"",
                    ""format"": ""csv"",
                    ""sources"": [
                        {
                            ""title"": ""My article"",
                            ""path"": ""https://example.com/article.html""
                        },
                        {
                            ""title"": ""My source"",
                            ""email"": ""john.doe@company.com""
                        }
                    ]
                }
            ]
        }"));
        var factory = new DataPackageFactory();
        var dataPackage = factory.LoadFromStream(stream);
        Assert.That(dataPackage, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataPackage.Name, Is.EqualTo("my-data-package"));
            Assert.That(dataPackage.Resources, Has.Count.EqualTo(1));
        });
        Assert.That(dataPackage.Resources[0].Sources, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(dataPackage.Resources[0].Sources[0].Title, Is.EqualTo("My article"));
            Assert.That(dataPackage.Resources[0].Sources[0].Path, Is.EqualTo("https://example.com/article.html"));
            Assert.That(dataPackage.Resources[0].Sources[1].Title, Is.EqualTo("My source"));
            Assert.That(dataPackage.Resources[0].Sources[1].Email, Is.EqualTo("john.doe@company.com"));
        });
    }

    [Test]
    public void LoadFromStream_WithEmbeddedFile_CorrectPackageInfo()
    {
        string resourceName = $"{GetType().Namespace}.Resources.example.json";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        var factory = new DataPackageFactory();
        var dataPackage = factory.LoadFromStream(stream);
        Assert.That(dataPackage, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataPackage.Name, Is.EqualTo("example_package"));
            Assert.That(dataPackage.Id, Is.EqualTo("115f49c1-8603-463e-a908-68de98327266"));
            Assert.That(dataPackage.Version, Is.EqualTo("2.0"));
            Assert.That(dataPackage.Created, Is.EqualTo(new DateTime(2024, 08, 27, 12, 45, 21)));
            Assert.That(dataPackage.Image, Is.Null);
        });
        Assert.That(dataPackage.Id, Is.EqualTo("115f49c1-8603-463e-a908-68de98327266"));
    }

    [Test]
    public void LoadFromStream_WithEmbeddedFile_ReturnLicences()
    {
        string resourceName = $"{GetType().Namespace}.Resources.example.json";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        var factory = new DataPackageFactory();
        var dataPackage = factory.LoadFromStream(stream);
        Assert.That(dataPackage.Licenses, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(dataPackage.Licenses[0].Name, Is.EqualTo("CC0-1.0"));
            Assert.That(dataPackage.Licenses[0].Path, Is.EqualTo("https://creativecommons.org/publicdomain/zero/1.0/"));
            Assert.That(dataPackage.Licenses[0].Title, Is.EqualTo("CC0 1.0"));
        });
    }

    [Test]
    public void LoadFromStream_WithEmbeddedFile_ReturnsResources()
    {
        string resourceName = $"{GetType().Namespace}.Resources.example.json";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        var factory = new DataPackageFactory();
        var dataPackage = factory.LoadFromStream(stream);
        Assert.That(dataPackage.Resources, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(dataPackage.Resources[0].Profile, Is.EqualTo("https://datapackage.org/profiles/2.0/dataresource.json"));
            Assert.That(dataPackage.Resources[0].Name, Is.EqualTo("deployments"));
            Assert.That(dataPackage.Resources[0].Paths.Select(p => p.ToString()), Does.Contain("deployments.csv"));
            Assert.That(dataPackage.Resources[0].Type, Is.EqualTo("table"));
            Assert.That(dataPackage.Resources[0].Title, Is.EqualTo("Camera trap deployments"));
            Assert.That(dataPackage.Resources[0].Format, Is.EqualTo("csv"));
            Assert.That(dataPackage.Resources[0].MediaType, Is.EqualTo("text/csv"));
            Assert.That(dataPackage.Resources[0].Encoding, Is.EqualTo("utf-8"));
            Assert.That(dataPackage.Resources[0].Dialect, Is.Null);
        });
    }

    [Test]
    public void LoadFromStream_WithEmbeddedFile_ReturnsTableDialect()
    {
        string resourceName = $"{GetType().Namespace}.Resources.example.json";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        var factory = new DataPackageFactory();
        var dataPackage = factory.LoadFromStream(stream);
        Assert.That(dataPackage.Resources, Has.Count.EqualTo(3));
        Assert.That(dataPackage.Resources[1].Dialect, Is.Not.Null);
        var dialect = dataPackage.Resources[1].Dialect!;
        Assert.Multiple(() =>
        {
            Assert.That(dialect.Profile, Is.EqualTo("https://datapackage.org/profiles/2.0/tabledialect.json"));
            Assert.That(dialect.Delimiter, Is.EqualTo('\t'));
            Assert.That(dialect.LineTerminator, Is.EqualTo("\r\n"));
        });
    }

    [Test]
    public void LoadFromStream_WithEmbeddedFile_ReturnsSchema()
    {
        string resourceName = $"{GetType().Namespace}.Resources.example.json";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        var factory = new DataPackageFactory();
        var dataPackage = factory.LoadFromStream(stream);
        Assert.That(dataPackage.Resources, Has.Count.EqualTo(3));
        Assert.That(dataPackage.Resources[0].Schema, Is.Not.Null);
        var schema = dataPackage.Resources[0].Schema!;
        Assert.That(schema.Fields, Has.Count.EqualTo(5));
        Assert.Multiple(() =>
        {
            Assert.That(schema.Profile, Is.EqualTo("https://datapackage.org/profiles/2.0/tableschema.json"));
            Assert.That(schema.Fields[0], Is.TypeOf<StringField>());
            Assert.That(schema.Fields[0].Name, Is.EqualTo("deployment_id"));
            Assert.That(schema.Fields[0].Type, Is.EqualTo("string"));
            Assert.That(schema.Fields[1], Is.TypeOf<NumberField>());
            Assert.That(schema.Fields[1].Name, Is.EqualTo("longitude"));
            Assert.That(schema.Fields[1].Type, Is.EqualTo("number"));
            Assert.That(schema.Fields[2], Is.TypeOf<Field>());
            Assert.That(schema.Fields[2].Name, Is.EqualTo("latitude"));
            Assert.That(schema.Fields[2].Type, Is.Null);
            Assert.That(schema.Fields[3], Is.TypeOf<DateField>());
            Assert.That(schema.Fields[3].Name, Is.EqualTo("start"));
            Assert.That(schema.Fields[3].Type, Is.EqualTo("date"));
            Assert.That(schema.Fields[3].Format, Is.EqualTo("%x"));
            Assert.That(schema.Fields[4], Is.TypeOf<StringField>());
            Assert.That(schema.Fields[4].Name, Is.EqualTo("comments"));
            Assert.That(schema.Fields[4].Type, Is.EqualTo("string"));
        });
    }

    [Test]
    public void LoadFromStream_WithValidStream_ReturnsSchema()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"{
            ""name"": ""my-data-package"",
            ""resources"": [
                {
                    ""name"": ""data.csv"",
                    ""path"": ""https://example.com/data.csv"",

                    ""schema"": {
                        ""fieldsMatch"": ""equal"",
                        ""fields"": [
                            {
                                ""name"": ""field_integer"",
                                ""type"": ""integer"",
                                ""bareNumber"": false,
                                ""groupChar"": "",""
                            },
                            {
                                ""name"": ""field_number"",
                                ""type"": ""number"",
                                ""bareNumber"": true,
                                ""groupChar"": "" "",
                                ""decimalChar"": "".""
                            },
                            {
                                ""name"": ""field_date"",
                                ""type"": ""date"",
                                ""format"": ""%Y-%m-%d""
                            },
                            {
                                ""name"": ""field_time"",
                                ""type"": ""time"",
                                ""format"": ""%H:%M:%S""
                            },
                            {
                                ""name"": ""field_year"",
                                ""type"": ""year""
                            },
                            {
                                ""name"": ""field_yearmonth"",
                                ""type"": ""yearmonth""
                            },
                            {
                                ""name"": ""field_boolean"",
                                ""type"": ""boolean""
                            },
                            {
                                ""name"": ""field_object"",
                                ""type"": ""object""
                            }
                        ],
                        ""$schema"": ""https://datapackage.org/profiles/2.0/tableschema.json"",
                    }
                }
            ]
        }"));
        var factory = new DataPackageFactory();
        var dataPackage = factory.LoadFromStream(stream);
        Assert.That(dataPackage.Resources[0].Schema, Is.Not.Null);
        var schema = dataPackage.Resources[0].Schema!;
        Assert.That(schema.Fields, Has.Count.EqualTo(8));
        Assert.Multiple(() =>
        {
            Assert.That(schema.Profile, Is.EqualTo("https://datapackage.org/profiles/2.0/tableschema.json"));
            Assert.That(schema.FieldsMatch, Is.EqualTo(FieldsMatching.Equal));
            Assert.That(schema.Fields[0], Is.TypeOf<IntegerField>());
            Assert.That(((IntegerField)schema.Fields[0]).BareNumber, Is.False);
            Assert.That(((IntegerField)schema.Fields[0]).GroupChar, Is.EqualTo(','));
            Assert.That(schema.Fields[1], Is.TypeOf<NumberField>());
            Assert.That(((NumberField)schema.Fields[1]).BareNumber, Is.True);
            Assert.That(((NumberField)schema.Fields[1]).GroupChar, Is.EqualTo(' '));
            Assert.That(((NumberField)schema.Fields[1]).DecimalChar, Is.EqualTo('.'));
            Assert.That(schema.Fields[2], Is.TypeOf<DateField>());
            Assert.That(schema.Fields[2].Format, Is.EqualTo("%Y-%m-%d"));
            Assert.That(schema.Fields[3], Is.TypeOf<TimeField>());
            Assert.That(schema.Fields[3].Format, Is.EqualTo("%H:%M:%S"));
            Assert.That(schema.Fields[4], Is.TypeOf<YearField>());
            Assert.That(schema.Fields[5], Is.TypeOf<YearMonthField>());
            Assert.That(schema.Fields[6], Is.TypeOf<BooleanField>());
            Assert.That(schema.Fields[7], Is.TypeOf<ObjectField>());
        });
    }

    [Test]
    public void LoadFromStream_WithValidStream_ReturnsResources()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"{
            ""name"": ""my-data-package"",
            ""resources"": [
                {
                    ""name"": ""data.csv"",
                    ""path"": ""https://example.com/data.csv"",
                    ""description"": ""A really long description"",
                    ""bytes"": 752 ,
                    ""hash"": ""2bf9cebe5915601985c8febd3d3d37d1"",
                }
            ]
        }"));
        var factory = new DataPackageFactory();
        var dataPackage = factory.LoadFromStream(stream);
        Assert.That(dataPackage, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(dataPackage.Name, Is.EqualTo("my-data-package"));
            Assert.That(dataPackage.Resources, Has.Count.EqualTo(1));
        });
        Assert.Multiple(() =>
        {
            Assert.That(dataPackage.Resources[0].Name, Is.EqualTo("data.csv"));
            Assert.That(dataPackage.Resources[0].Paths.Select(p => p.ToString()), Does.Contain("https://example.com/data.csv"));
            Assert.That(dataPackage.Resources[0].Description, Is.EqualTo("A really long description"));
            Assert.That(dataPackage.Resources[0].Bytes, Is.EqualTo(752));
            Assert.That(dataPackage.Resources[0].Hash, Is.EqualTo("2bf9cebe5915601985c8febd3d3d37d1"));
        });
    }

    [Test]
    public void LoadFromStream_WithResources_ReturnsResourcePaths()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"{
            ""name"": ""my-data-package"",
            ""resources"": [
                {
                    ""name"": ""data"",
                    ""path"": [""data_1.csv"", ""data_2.csv""],
                    ""format"": ""csv"",
                }
            ]
        }"));
        var factory = new DataPackageFactory();
        var dataPackage = factory.LoadFromStream(stream);
        Assert.That(dataPackage, Is.Not.Null);
        Assert.That(dataPackage.Resources[0], Is.Not.Null);
        Assert.That(dataPackage.Resources[0].Paths, Is.Not.Null.Or.Empty);
        Assert.That(dataPackage.Resources[0].Paths.Select(p => p.ToString()), Does.Contain("data_1.csv"));
        Assert.That(dataPackage.Resources[0].Paths.Select(p => p.ToString()), Does.Contain("data_2.csv"));
    }

    [Test]
    public void LoadFromStream_WithMissingValueAsStringArray_ReturnsSchema()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"{
            ""name"": ""my-data-package"",
            ""resources"": [
                {
                    ""name"": ""data.csv"",
                    ""path"": ""https://example.com/data.csv"",

                    ""schema"": {
                        ""fieldsMatch"": ""equal"",
                        ""fields"": [
                            {
                                ""name"": ""field_integer"",
                                ""type"": ""integer"",
                                ""bareNumber"": false,
                                ""groupChar"": "",""
                            }
                        ],
                        ""missingValues"": [""-"", ""NaN"", """"],
                        ""$schema"": ""https://datapackage.org/profiles/2.0/tableschema.json"",
                    }
                }
            ]
        }"));

        var factory = new DataPackageFactory();
        var dataPackage = factory.LoadFromStream(stream);
        Assert.That(dataPackage.Resources[0].Schema, Is.Not.Null);
        var schema = dataPackage.Resources[0].Schema!;
        Assert.That(schema.MissingValues, Has.Count.EqualTo(3));
        Assert.That(schema.MissingValues, Has.One.Property("Value").EqualTo("NaN"));
        Assert.That(schema.MissingValues, Has.All.Property("Label").Null);
    }

    [Test]
    public void LoadFromStream_WithMissingValueAsObjectArray_ReturnsSchema()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"{
            ""name"": ""my-data-package"",
            ""resources"": [
                {
                    ""name"": ""data.csv"",
                    ""path"": ""https://example.com/data.csv"",

                    ""schema"": {
                        ""fieldsMatch"": ""equal"",
                        ""fields"": [
                            {
                                ""name"": ""field_integer"",
                                ""type"": ""integer"",
                                ""bareNumber"": false,
                                ""groupChar"": "",""
                            }
                        ],
                        ""missingValues"": [
                            {
                                ""value"": ""-"",
                                ""label"": ""Missing value""
                            },
                            {
                                ""value"": ""NaN"",
                                ""label"": ""Not a number""
                            },
                            {
                                ""value"": """",
                                ""label"": ""Unknown""
                            }
                        ],
                        ""$schema"": ""https://datapackage.org/profiles/2.0/tableschema.json"",
                    }
                }
            ]
        }"));

        var factory = new DataPackageFactory();
        var dataPackage = factory.LoadFromStream(stream);
        Assert.That(dataPackage.Resources[0].Schema, Is.Not.Null);
        var schema = dataPackage.Resources[0].Schema!;
        Assert.That(schema.MissingValues, Has.Count.EqualTo(3));
        Assert.That(schema.MissingValues, Has.One.Property("Value").EqualTo("NaN"));
        Assert.That(schema.MissingValues, Has.All.Property("Label").Not.Null);
    }

    [Test]
    public void LoadFromStream_WithKeys_ReturnsKeys()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"{
            ""name"": ""my-data-package"",
            ""resources"": [
                {
                    ""name"": ""data.csv"",
                    ""path"": ""https://example.com/data.csv"",

                    ""schema"": {
                        ""fieldsMatch"": ""equal"",
                        ""fields"": [
                            {
                                ""name"": ""name"",
                                ""type"": ""string""
                            },
                            {
                                ""name"": ""fk"",
                                ""type"": ""integer""
                            },
                            {
                                ""name"": ""info"",
                                ""type"": ""string""
                            },
                            {
                                ""name"": ""parent"",
                                ""type"": ""string""
                            }
                        ],
                        ""primaryKey"": [""name""],
                        ""uniqueKeys"": [
                            [""name""],
                            [""fk"", ""info""]
                        ],
                        ""foreignKeys"": [
                            {
                                ""fields"": [""fk""],
                                ""reference"": {
                                    ""resource"": ""other.csv"",
                                    ""fields"": [""id""]
                                }
                            },
                            {
                                ""fields"": [""parent""],
                                ""reference"": {
                                    ""fields"": [""name""]
                                }
                            }   
                        ],
                        ""$schema"": ""https://datapackage.org/profiles/2.0/tableschema.json"",
                    }
                }
            ]
        }"));
        var factory = new DataPackageFactory();
        var dataPackage = factory.LoadFromStream(stream);
        Assert.That(dataPackage, Is.Not.Null);
        Assert.That(dataPackage.Resources[0]?.Schema, Is.Not.Null);
        var schema = dataPackage.Resources[0].Schema!;

        Assert.That(schema.PrimaryKey, Has.Count.EqualTo(1));
        Assert.That(schema.PrimaryKey[0], Is.EqualTo("name"));

        Assert.That(schema.UniqueKeys, Has.Count.EqualTo(2));
        Assert.That(schema.UniqueKeys[0], Has.Count.EqualTo(1));
        Assert.That(schema.UniqueKeys[0][0], Is.EqualTo("name"));
        Assert.That(schema.UniqueKeys[1], Has.Count.EqualTo(2));
        Assert.That(schema.UniqueKeys[1], Does.Contain("fk"));
        Assert.That(schema.UniqueKeys[1], Does.Contain("info"));

        Assert.That(schema.ForeignKeys, Has.Count.EqualTo(2));
        Assert.That(schema.ForeignKeys[0].Fields, Has.Count.EqualTo(1));
        Assert.That(schema.ForeignKeys[0].Fields[0], Is.EqualTo("fk"));
        Assert.That(schema.ForeignKeys[0].Reference, Is.Not.Null);
        Assert.That(schema.ForeignKeys[0].Reference.Resource, Is.EqualTo("other.csv"));
        Assert.That(schema.ForeignKeys[0].Reference.Fields, Has.Count.EqualTo(1));
        Assert.That(schema.ForeignKeys[0].Reference.Fields[0], Is.EqualTo("id"));
        Assert.That(schema.ForeignKeys[1].Fields, Has.Count.EqualTo(1));
        Assert.That(schema.ForeignKeys[1].Fields[0], Is.EqualTo("parent"));
        Assert.That(schema.ForeignKeys[1].Reference, Is.Not.Null);
        Assert.That(schema.ForeignKeys[1].Reference.Resource, Is.Null);
        Assert.That(schema.ForeignKeys[1].Reference.Fields, Has.Count.EqualTo(1));
        Assert.That(schema.ForeignKeys[1].Reference.Fields[0], Is.EqualTo("name"));
    }
}
