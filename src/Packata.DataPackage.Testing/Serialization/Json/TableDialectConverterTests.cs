using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Packata.Core.Storage;
using Packata.Core.Serialization.Json;
using NUnit.Framework;
using System.Net.Http;
using Newtonsoft.Json.Serialization;

namespace Packata.Core.Testing.Serialization.Json;

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
            Assert.That(delimitedDialect.Delimiter, Is.EqualTo(';'));
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
            Assert.That(delimitedDialect.Delimiter, Is.EqualTo(';'));
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
}
