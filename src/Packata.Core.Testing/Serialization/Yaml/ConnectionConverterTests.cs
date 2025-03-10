using System;
using System.Collections.Generic;
using Packata.Core.Serialization.Yaml;
using NUnit.Framework;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Packata.Core.Testing.Serialization.Yaml;

public class ConnectionConverterTests
{
    private class ConnectionWrapper
    {
        public IConnection? Connection { get; set; }
    }

    [Test]
    public void Accepts_FieldType_ReturnsTrue()
    {
        var converter = new ConnectionConverter();
        Assert.That(converter.Accepts(typeof(IConnection)), Is.True);
    }

    [Test]
    public void ReadJson_ValidJsonString_ReturnsCorrectValue()
    {
        var yaml = @"connection: mssql://server/db";

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new ConnectionConverter())
            .Build();

        var wrapper = deserializer.Deserialize<ConnectionWrapper>(yaml);

        Assert.That(wrapper?.Connection, Is.Not.Null);
        Assert.That(wrapper.Connection, Is.TypeOf<LiteralConnectionUrl>());
        Assert.That(wrapper.Connection.ConnectionUrl, Is.EqualTo("mssql://server/db"));
    }
}
