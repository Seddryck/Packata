using System;
using Packata.Core.Serialization;
using Json = Packata.Core.Serialization.Json;
using Yaml = Packata.Core.Serialization.Yaml;
using NUnit;
using NUnit.Framework;

public class SerializerFactoryTests
{
    [Test]
    public void Instantiate_ShouldReturnJsonSerializer_WhenFormatIsJson()
    {
        var serializer = new SerializerFactory().Instantiate("json");
        Assert.That(serializer, Is.TypeOf<Json.DataPackageSerializer>());
    }

    [Test]
    [TestCase("yaml")]
    [TestCase("yml")]
    public void Instantiate_ShouldReturnYamlSerializer_WhenFormatIsYaml(string format)
    {
        var serializer = new SerializerFactory().Instantiate(format);
        Assert.That(serializer, Is.TypeOf<Yaml.DataPackageSerializer>());
    }

    [Test]
    public void Instantiate_ShouldThrowArgumentOutOfRangeException_WhenFormatIsUnknown()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new SerializerFactory().Instantiate("unknown"));
}
