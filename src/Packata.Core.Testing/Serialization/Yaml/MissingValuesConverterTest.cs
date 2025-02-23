using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Packata.Core.Serialization.Yaml;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Packata.Core.Testing.Serialization.Yaml;
public class MissingValuesConverterTest
{
    private class MissingValuesWrapper
    {
        public string Id { get; set; } = string.Empty;
        public List<MissingValue>? MissingValues { get; set; }
    }

    [Test]
    [TestCase("id: 1\r\nmissingValues:\r\n  - \"\"\r\n  - NA\r\n  - NaN\r\n")]
    [TestCase("id: 2\r\nmissingValues:\r\n  - value: \"\"\r\n    label: blue\r\n  - value: \"NA\"\r\n    label: blue\r\n  - value: \"NaN\"\r\n    label: red\r\n")]
    public void DeserializeYamlWithMissingValues(string yaml)
    {
        var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithTypeConverter(new MissingValuesConverter())
                .Build();

        var result = deserializer.Deserialize<MissingValuesWrapper>(yaml);
        Assert.That(result!.MissingValues, Has.Count.EqualTo(3));
        Assert.That(result!.MissingValues, Has.One.Property(nameof(MissingValue.Value)).EqualTo("NaN"));
        Assert.That(result!.MissingValues, Has.One.Property(nameof(MissingValue.Value)).EqualTo(string.Empty));
        Assert.That(result!.MissingValues, Has.One.Property(nameof(MissingValue.Value)).EqualTo("NA"));
    }
}
