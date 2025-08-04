using System;
using System.Collections.Generic;
using Packata.Core.Serialization.Yaml;
using NUnit.Framework;
using YamlDotNet.Serialization;

namespace Packata.Core.Testing.Serialization.Yaml;

internal class TableDialectConverterTests : BaseTypeDiscriminatorTests<TableDialectTypeDiscriminator, TableDialect>
{
    public TableDialectConverterTests()
        : base("dialect")
    { }

    [Test]
    public void ReadJson_TypeDelimited_ReturnsCorrectValue()
    {
        var yaml = @"
                    dialect:
                        type: delimited
                        delimiter: "";""
                    ";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper?.Object, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Is.TypeOf<TableDelimitedDialect>());
            var dialect = (TableDelimitedDialect)wrapper.Object!;
            Assert.That(dialect.Delimiter, Is.EqualTo(';'));
        }
    }

    [Test]
    [Ignore("missing dialect type is not supported for Yaml")]
    public void ReadJson_TypeMissing_ReturnsCorrectValue()
    {
        var yaml = @"
                    dialect:
                        delimiter: "";""
                    ";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper?.Object, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Is.TypeOf<TableDelimitedDialect>());
            var dialect = (TableDelimitedDialect)wrapper.Object!;
            Assert.That(dialect.Delimiter, Is.EqualTo(';'));
        }
    }

    [Test]
    public void ReadJson_TypeDatabase_ReturnsCorrectValue()
    {
        var yaml = @"
                    dialect:
                        type: database
                        table: Customer
                        namespace: Sales
                    ";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Is.TypeOf<TableDatabaseDialect>());
            var dialect = (TableDatabaseDialect)wrapper.Object!;
            Assert.That(dialect.Table, Is.EqualTo("Customer"));
            Assert.That(dialect.Namespace, Is.EqualTo("Sales"));
        }
    }

    [Test]
    public void ReadJson_TypeSpreadsheet_ReturnsCorrectValue()
    {
        var yaml = @"
                    dialect:
                        type: spreadsheet
                        sheetName: Customer
                    ";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

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
