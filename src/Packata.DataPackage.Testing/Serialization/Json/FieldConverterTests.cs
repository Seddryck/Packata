using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Packata.Core.Serialization.Json;
using NUnit.Framework;

namespace Packata.Core.Testing.Serialization.Json;

internal class FieldConverterTests : BaseConverterTests<FieldConverter, List<Field>>
{
    public FieldConverterTests()
        : base("fields")
    { }

    [Test]
    public void ReadJson_ValidJson_ReturnsCorrectFieldList()
    {
        var json = @"{""fields"":
            [
                { ""type"": ""string"", ""name"": ""test"" },
                { ""type"": ""number"", ""name"": 123 },
                { ""type"": ""boolean"",""name"": true }
            ]}";

        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper!.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(3));
            Assert.That(wrapper.Object[0], Is.TypeOf<StringField>());
            Assert.That(wrapper.Object[1], Is.TypeOf<NumberField>());
            Assert.That(wrapper.Object[2], Is.TypeOf<BooleanField>());
        }
    }
}
