using System;
using Packata.Core.Serialization;
using SerJson = Packata.Core.Serialization.Json;
using SerYaml = Packata.Core.Serialization.Yaml;
using NUnit;
using NUnit.Framework;

namespace Packata.Core.Testing.Serialization;

public class SerializerFactoryTests
{
    [Test]
    public void Instantiate_ShouldReturnJsonSerializer_WhenFormatIsJson()
    {
        var serializer = new SerializerFactory().Instantiate("json");
        Assert.That(serializer, Is.TypeOf<SerJson.DataPackageSerializer>());
    }

    [Test]
    [TestCase("yaml")]
    [TestCase("yml")]
    public void Instantiate_ShouldReturnYamlSerializer_WhenFormatIsYaml(string format)
    {
        var serializer = new SerializerFactory().Instantiate(format);
        Assert.That(serializer, Is.TypeOf<SerYaml.DataPackageSerializer>());
    }

    [Test]
    public void Instantiate_ShouldThrowArgumentOutOfRangeException_WhenFormatIsUnknown()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new SerializerFactory().Instantiate("unknown"));
}
