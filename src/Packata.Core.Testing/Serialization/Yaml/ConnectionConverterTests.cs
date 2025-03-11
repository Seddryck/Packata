using System;
using System.Collections.Generic;
using Packata.Core.Serialization.Yaml;
using NUnit.Framework;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Packata.Core.Testing.Serialization.Yaml;

internal class ConnectionConverterTests : BaseConverterTests<ConnectionConverter, IConnection>
{
    public ConnectionConverterTests()
        : base("connection")
    { }

    [Test]
    public void ReadJson_ValidJsonString_ReturnsCorrectValue()
    {
        var yaml = @"connection: mssql://server/db";

        var wrapper = Deserializer.Deserialize<Wrapper>(yaml);

        Assert.That(wrapper?.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Is.TypeOf<LiteralConnectionUrl>());
            Assert.That(wrapper.Object.ConnectionUrl, Is.EqualTo("mssql://server/db"));
        }
    }
}
