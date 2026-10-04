using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.Storage;
using Packata.DataPackage.Serialization.Json;
using NUnit.Framework;
using System.Net.Http;
using Newtonsoft.Json.Serialization;

namespace Packata.DataPackage.Testing.Serialization.Json;

internal class TableDialectConverterTests : BaseConverterTests<TableDialectConverter, TableDialect>
{
    public TableDialectConverterTests()
        : base("dialect")
    { }

    [Test]
    public void ReadJson_TypeDelimited_ReturnsDelimited()
    {
        var json = @"{""dialect"":
            {
                ""$schema"": ""https://datapackage.org/profiles/2.0/tabledialect.json"",
                ""type"": ""delimited"",
                ""delimiter"": "";""
            }}";
        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper?.Object, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Is.TypeOf<TableDelimitedDialect>());
            var delimitedDialect = (TableDelimitedDialect)wrapper.Object!;
            Assert.That(delimitedDialect.Delimiter, Is.EqualTo(";"));
        }
    }

    [Test]
    public void ReadJson_TypeMissing_ReturnsDelimited()
    {
        var json = @"{""dialect"":
            {
                ""$schema"": ""https://datapackage.org/profiles/2.0/tabledialect.json"",
                ""delimiter"": "";""
            }}";
        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Is.TypeOf<TableDelimitedDialect>());
            var delimitedDialect = (TableDelimitedDialect)wrapper.Object!;
            Assert.That(delimitedDialect.Delimiter, Is.EqualTo(";"));
        }
    }

    [Test]
    public void ReadJson_TypeDatabase_ReturnsDatabase()
    {
        var json = @"{""dialect"":
            {
                ""$schema"": ""https://datapackage.org/profiles/2.0/tabledialect.json"",
                ""type"": ""database"",
                ""table"": ""Customer"",
                ""namespace"": ""dbo""
            }}";
        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Is.TypeOf<TableDatabaseDialect>());
            var dbDialect = (TableDatabaseDialect)wrapper.Object!;
            Assert.That(dbDialect.Table, Is.EqualTo("Customer"));
            Assert.That(dbDialect.Namespace, Is.EqualTo("dbo"));
        }
    }

    [Test]
    public void ReadJson_TypeSpreadsheet_ReturnsSpreadsheet()
    {
        var json = @"{""dialect"":
            {
                ""$schema"": ""https://datapackage.org/profiles/2.0/tabledialect.json"",
                ""type"": ""spreadsheet"",
                ""sheetName"": ""Customer""
            }}";
        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Is.TypeOf<TableSpreadsheetDialect>());
            var dialect = (TableSpreadsheetDialect)wrapper.Object!;
            Assert.That(dialect.SheetName, Is.EqualTo("Customer"));
            Assert.That(dialect.SheetNumber, Is.Null);
        }
    }

    [Test]
    public void ReadJson_StructuredPropertiesWithoutType_ReturnsStructuredDialect()
    {
        const string json = """
            {"dialect":{"property":"rows","itemType":"object","itemKeys":["id","name"]}}
            """;

        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper!.Object, Is.TypeOf<TableStructuredDialect>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object!.Property, Is.EqualTo("rows"));
            Assert.That(wrapper.Object.ItemType, Is.EqualTo("object"));
            Assert.That(wrapper.Object.ItemKeys, Is.EqualTo(new[] { "id", "name" }));
        }
    }

    [Test]
    public void ReadJson_MultiCharacterDelimiter_PreservesValue()
    {
        const string json = """{"dialect":{"delimiter":"||","commentChar":"//"}}""";

        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper!.Object!.Delimiter, Is.EqualTo("||"));
        Assert.That(wrapper.Object.CommentChar, Is.EqualTo("//"));
    }

    [TestCase("{\"dialect\":{\"sheetName\":\"Data\"}}", typeof(TableSpreadsheetDialect))]
    [TestCase("{\"dialect\":{\"table\":\"customers\"}}", typeof(TableDatabaseDialect))]
    public void ReadJson_FormatSpecificPropertyWithoutType_InfersDialect(string json, Type expectedType)
    {
        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper!.Object, Is.TypeOf(expectedType));
    }
}
