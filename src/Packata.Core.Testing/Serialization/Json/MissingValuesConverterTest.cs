using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Packata.Core.Serialization.Json;

namespace Packata.Core.Testing.Serialization.Json;
public class MissingValuesConverterTest
{
    private class MissingValuesWrapper
    {
        public string Id { get; set; } = string.Empty;
        public List<MissingValue>? MissingValues { get; set; }
    }

    [Test]
    [TestCase(@"{ ""id"": ""1"", ""missingValues"": ["""", ""NA"", ""NaN""] }")]
    [TestCase(@"{ ""id"": ""2"", ""missingValues"": [ { ""value"": """", ""label"": ""blue""}, { ""value"": ""NA"", ""label"": ""blue""},  { ""value"": ""NaN"", ""label"": ""red""}] }")]
    public void Debug(string json)
    {
        var settings = new JsonSerializerSettings();
        settings.Converters.Add(new MissingValuesConverter());

        var result = JsonConvert.DeserializeObject<MissingValuesWrapper>(json, settings);
        Assert.That(result!.MissingValues, Has.Count.EqualTo(3));
        Assert.That(result!.MissingValues, Has.One.Property(nameof(MissingValue.Value)).EqualTo("NaN"));
        Assert.That(result!.MissingValues, Has.One.Property(nameof(MissingValue.Value)).EqualTo(string.Empty));
        Assert.That(result!.MissingValues, Has.One.Property(nameof(MissingValue.Value)).EqualTo("NA"));
    }
}
