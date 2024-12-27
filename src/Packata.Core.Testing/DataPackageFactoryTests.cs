using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

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
    public void LoadFromStream_WithValidStream_ReturnsResourcePaths()
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
}
