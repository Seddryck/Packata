using System;
using System.Collections.Generic;
using Packata.Core.Serialization.Yaml;
using NUnit.Framework;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Packata.Core.Testing.Serialization.Yaml;

public class TableDialectTypeDiscriminatorTests
{
    private class DialectWrapper
    {
        public TableDialect? Dialect { get; set; }
    }

    [Test]
    public void ReadJson_TypeDelimited_ReturnsCorrectValue()
    {
        var yaml = @"
                    dialect:
                        $schema: https://datapackage.org/profiles/2.0/tabledialect.json
                        type: delimited
                        delimiter: "";""
                    ";

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(new DataPackageNamingConvention())
            .WithTypeDiscriminatingNodeDeserializer((o) => new TableDialectTypeDiscriminator().Execute(o))
            .Build();

        var wrapper = deserializer.Deserialize<DialectWrapper>(yaml);

        Assert.That(wrapper?.Dialect, Is.Not.Null);
        Assert.That(wrapper.Dialect, Is.TypeOf<TableDelimitedDialect>());
        var dialect = (TableDelimitedDialect)wrapper.Dialect;
        Assert.That(dialect.Delimiter, Is.EqualTo(';'));
    }

    [Test]
    [Ignore("missing dialect type is not supported for Yaml")]
    public void ReadJson_TypeMissing_ReturnsCorrectValue()
    {
        var yaml = @"
                    dialect:
                        $schema: https://datapackage.org/profiles/2.0/tabledialect.json
                        delimiter: "";""
                    ";

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(new DataPackageNamingConvention())
            .WithTypeDiscriminatingNodeDeserializer((o) => new TableDialectTypeDiscriminator().Execute(o))
            .Build();

        var wrapper = deserializer.Deserialize<DialectWrapper>(yaml);

        Assert.That(wrapper?.Dialect, Is.Not.Null);
        Assert.That(wrapper.Dialect, Is.TypeOf<TableDelimitedDialect>());
        var dialect = (TableDelimitedDialect)wrapper.Dialect;
        Assert.That(dialect.Delimiter, Is.EqualTo(';'));
    }

    [Test]
    public void ReadJson_TypeDatabase_ReturnsCorrectValue()
    {
        var yaml = @"
                    dialect:
                        $schema: https://datapackage.org/profiles/2.0/tabledialect.json
                        type: database
                        table: Customer
                    ";

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(new DataPackageNamingConvention())
            .WithTypeDiscriminatingNodeDeserializer((o) => new TableDialectTypeDiscriminator().Execute(o))
            .Build();

        var wrapper = deserializer.Deserialize<DialectWrapper>(yaml);

        Assert.That(wrapper?.Dialect, Is.Not.Null);
        Assert.That(wrapper.Dialect, Is.TypeOf<TableDatabaseDialect>());
        var dialect = (TableDatabaseDialect)wrapper.Dialect;
        Assert.That(dialect.Table, Is.EqualTo("Customer"));
    }
}
