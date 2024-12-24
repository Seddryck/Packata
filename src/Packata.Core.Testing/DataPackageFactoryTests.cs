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
            ""resources"": [
                {
                    ""name"": ""data.csv"",
                    ""path"": ""https://example.com/data.csv"",
                    ""format"": ""csv""
                }
            ],
            ""contributors"": [
                {
                    ""name"": ""Jane Doe"",
                    ""email"": ""jane.doe@company.com"",
                    ""role"": ""creator""
                }, 
                {
                    ""name"": ""John Doe"",
                    ""email"": ""john.doe@company.com""
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
            Assert.That(dataPackage.Keywords, Does.Contain("data"));
            Assert.That(dataPackage.Keywords, Does.Contain("example"));
            Assert.That(dataPackage.Resources, Has.Count.EqualTo(1));
            Assert.That(dataPackage.Contributors, Has.Count.EqualTo(2));
        });
        Assert.Multiple(() =>
        {
            Assert.That(dataPackage.Contributors[0].Name, Is.EqualTo("Jane Doe"));
            Assert.That(dataPackage.Contributors[0].Email, Is.EqualTo("jane.doe@company.com"));
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
            Assert.That(dataPackage.Resources[0].Path, Is.EqualTo("deployments.csv"));
            Assert.That(dataPackage.Resources[0].Type, Is.EqualTo("table"));
            Assert.That(dataPackage.Resources[0].Title, Is.EqualTo("Camera trap deployments"));
            Assert.That(dataPackage.Resources[0].Format, Is.EqualTo("csv"));
            Assert.That(dataPackage.Resources[0].MediaType, Is.EqualTo("text/csv"));
            Assert.That(dataPackage.Resources[0].Encoding, Is.EqualTo("utf-8"));
        });
    }
}
