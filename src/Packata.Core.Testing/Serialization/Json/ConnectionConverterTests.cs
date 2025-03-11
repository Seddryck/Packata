using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Packata.Core.Serialization.Json;
using NUnit.Framework;

namespace Packata.Core.Testing.Serialization.Json;

internal class ConnectionConverterTests : BaseConverterTests<ConnectionConverter, IConnection>
{
    public ConnectionConverterTests()
        : base("connection")
    { }

    [Test]
    public void ReadJson_ValidJsonString_ReturnsCorrectConnectionUrl()
    {
        var json = @"{""connection"": ""mssql://server/db""}";

        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper?.Object, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Is.TypeOf<LiteralConnectionUrl>());
            Assert.That(wrapper.Object.ConnectionUrl, Is.EqualTo("mssql://server/db"));
        }
    }
}
