using System;
using System.Collections.Generic;
using NUnit.Framework;
using Packata.Core.Serialization.Yaml;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Packata.Core.Testing.Serialization.Yaml;
internal class MissingValuesConverterTest : BaseConverterTests<MissingValuesConverter, List<MissingValue>>
{
    public MissingValuesConverterTest()
        : base("missingValues")
    { }

    [Test]
    [TestCase("missingValues:\r\n  - \"\"\r\n  - NA\r\n  - NaN\r\n")]
    [TestCase("missingValues:\r\n  - value: \"\"\r\n    label: blue\r\n  - value: \"NA\"\r\n    label: blue\r\n  - value: \"NaN\"\r\n    label: red\r\n")]
    public void DeserializeYamlWithMissingValues(string yaml)
    {
        
        var result = Deserializer.Deserialize<Wrapper>(yaml);
        Assert.That(result!.Object, Has.Count.EqualTo(3));
        Assert.That(result!.Object, Has.One.Property(nameof(MissingValue.Value)).EqualTo("NaN"));
        Assert.That(result!.Object, Has.One.Property(nameof(MissingValue.Value)).EqualTo(string.Empty));
        Assert.That(result!.Object, Has.One.Property(nameof(MissingValue.Value)).EqualTo("NA"));
    }
}
