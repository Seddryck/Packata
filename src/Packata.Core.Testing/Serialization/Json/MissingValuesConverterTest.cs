using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Packata.Core.Serialization.Json;

namespace Packata.Core.Testing.Serialization.Json;
internal class MissingValuesConverterTest : BaseConverterTests<MissingValuesConverter, List<MissingValue>>
{
    public MissingValuesConverterTest()
        : base("missingValues")
    { }

    [Test]
    [TestCase(@"{ ""missingValues"": ["""", ""NA"", ""NaN""] }")]
    [TestCase(@"{ ""missingValues"": [ { ""value"": """", ""label"": ""blue""}, { ""value"": ""NA"", ""label"": ""blue""},  { ""value"": ""NaN"", ""label"": ""red""}] }")]
    public void DeserializeObject_MissingValues_Expected(string json)
    {
        var wrapper = JsonConvert.DeserializeObject<Wrapper>(json, Settings);

        Assert.That(wrapper!.Object, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Object, Has.Count.EqualTo(3));
            Assert.That(wrapper.Object, Has.One.Property(nameof(MissingValue.Value)).EqualTo("NaN"));
            Assert.That(wrapper.Object, Has.One.Property(nameof(MissingValue.Value)).EqualTo(string.Empty));
            Assert.That(wrapper.Object, Has.One.Property(nameof(MissingValue.Value)).EqualTo("NA"));
        }
    }
}
