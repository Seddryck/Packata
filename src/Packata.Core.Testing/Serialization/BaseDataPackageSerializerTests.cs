using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.PlatformAbstractions.Interfaces;
using Newtonsoft.Json;
using NUnit.Framework;
using Packata.Core.Serialization;
using Packata.Core.Serialization.Json;

namespace Packata.Core.Testing.Serialization;

public abstract class BaseDataPackageSerializerTests
{
    protected abstract IDataPackageSerializer GetSerializer();

    protected abstract string GetFormat();

    protected abstract Stream GetDataPackageProperties();

    [Test]
    public void Deserialize_DataPackageProperties_ReturnsDataPackage()
    {
        using var stream = GetDataPackageProperties();
        using var streamReader = new StreamReader(stream);
        var dataPackage = GetSerializer().Deserialize(streamReader, new HttpClient(), "c:\\");
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

    protected abstract Stream GetContributorsProperties();

    [Test]
    public void Deserialize_Contributors_ReturnsContributors()
    {
        using var stream = GetContributorsProperties();
        using var streamReader = new StreamReader(stream);
        var dataPackage = GetSerializer().Deserialize(streamReader, new HttpClient(), "c:\\");
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

    protected abstract Stream GetSourceProperties();

    [Test]
    public void Deserialize_SourcesProperties_ReturnsResourceSources()
    {
        using var stream = GetSourceProperties();
        using var streamReader = new StreamReader(stream);
        var dataPackage = GetSerializer().Deserialize(streamReader, new HttpClient(), "c:\\");
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

    protected abstract Stream GetFieldsProperties();

    [Test]
    public void Deserialize_FieldsProperties_ReturnsSchema()
    {
        using var stream = GetFieldsProperties();
        using var streamReader = new StreamReader(stream);
        var dataPackage = GetSerializer().Deserialize(streamReader, new HttpClient(), "c:\\");
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

    protected abstract Stream GetResourcesProperties();

    [Test]
    public void Deserialize_ResourcesPath_ReturnsResources()
    {
        using var stream = GetResourcesProperties();
        using var streamReader = new StreamReader(stream);
        var dataPackage = GetSerializer().Deserialize(streamReader, new HttpClient(), "c:\\");
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

    protected abstract Stream GetResourcesPathProperties();

    [Test]
    public void Deserialize_ResourcesPath_ReturnsResourcePaths()
    {
        using var stream = GetResourcesPathProperties();
        using var streamReader = new StreamReader(stream);
        var dataPackage = GetSerializer().Deserialize(streamReader, new HttpClient(), "c:\\");
        Assert.That(dataPackage, Is.Not.Null);
        Assert.That(dataPackage.Resources[0], Is.Not.Null);
        Assert.That(dataPackage.Resources[0].Paths, Is.Not.Null.Or.Empty);
        Assert.That(dataPackage.Resources[0].Paths.Select(p => p.ToString()), Does.Contain("data_1.csv"));
        Assert.That(dataPackage.Resources[0].Paths.Select(p => p.ToString()), Does.Contain("data_2.csv"));
    }

    protected abstract Stream GetMissingValuesAsStringArrayProperties();

    [Test]
    public void Deserialize_WithMissingValueAStringArray_ReturnsSchema()
    {
        using var stream = GetMissingValuesAsStringArrayProperties();
        using var streamReader = new StreamReader(stream);
        var dataPackage = GetSerializer().Deserialize(streamReader, new HttpClient(), "c:\\");
        Assert.That(dataPackage.Resources[0].Schema, Is.Not.Null);
        var schema = dataPackage.Resources[0].Schema!;
        Assert.That(schema.MissingValues, Has.Count.EqualTo(3));
        Assert.That(schema.MissingValues, Has.One.Property("Value").EqualTo("NaN"));
        Assert.That(schema.MissingValues, Has.All.Property("Label").Null);
    }

    protected abstract Stream GetMissingValuesAsObjectsProperties();

    [Test]
    public void Deserialize_WithMissingValueAsObjectArray_ReturnsSchema()
    {
        using var stream = GetMissingValuesAsObjectsProperties();
        using var streamReader = new StreamReader(stream);
        var dataPackage = GetSerializer().Deserialize(streamReader, new HttpClient(), "c:\\");
        Assert.That(dataPackage.Resources[0].Schema, Is.Not.Null);
        var schema = dataPackage.Resources[0].Schema!;
        Assert.That(schema.MissingValues, Has.Count.EqualTo(3));
        Assert.That(schema.MissingValues, Has.One.Property("Value").EqualTo("NaN"));
        Assert.That(schema.MissingValues, Has.All.Property("Label").Not.Null);
    }

    protected abstract Stream GetKeysProperties();

    [Test]
    public void Deserialize_WithKeys_ReturnsKeys()
    {
        using var stream = GetKeysProperties();
        using var streamReader = new StreamReader(stream);
        var dataPackage = GetSerializer().Deserialize(streamReader, new HttpClient(), "c:\\");
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


    [Test]
    public void Deserialize_EmbeddedFile_CorrectPackageInfo()
    {
        string resourceName = $"{GetType().Namespace}.Resources.example.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        using var streamReader = new StreamReader(stream);
        var dataPackage = GetSerializer().Deserialize(streamReader, new HttpClient(), "c:\\");
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
    public void Deserialize_EmbeddedFile_ReturnLicences()
    {
        string resourceName = $"{GetType().Namespace}.Resources.example.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");


        using var streamReader = new StreamReader(stream);
        var dataPackage = GetSerializer().Deserialize(streamReader, new HttpClient(), "c:\\");
        Assert.That(dataPackage.Licenses, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(dataPackage.Licenses[0].Name, Is.EqualTo("CC0-1.0"));
            Assert.That(dataPackage.Licenses[0].Path, Is.EqualTo("https://creativecommons.org/publicdomain/zero/1.0/"));
            Assert.That(dataPackage.Licenses[0].Title, Is.EqualTo("CC0 1.0"));
        });
    }

    [Test]
    public void Deserialize_EmbeddedFile_ReturnsResources()
    {
        string resourceName = $"{GetType().Namespace}.Resources.example.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        using var streamReader = new StreamReader(stream);
        var dataPackage = GetSerializer().Deserialize(streamReader, new HttpClient(), "c:\\");
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
    public void Deserialize_EmbeddedFile_ReturnsTableDialect()
    {
        string resourceName = $"{GetType().Namespace}.Resources.example.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        using var streamReader = new StreamReader(stream);
        var dataPackage = GetSerializer().Deserialize(streamReader, new HttpClient(), "c:\\");
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
    public void Deserialize_EmbeddedFile_ReturnsSchema()
    {
        string resourceName = $"{GetType().Namespace}.Resources.example.{GetFormat()}";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"The embedded file {resourceName} doesn't exist.");

        using var streamReader = new StreamReader(stream);
        var dataPackage = GetSerializer().Deserialize(streamReader, new HttpClient(), "c:\\");
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
}
