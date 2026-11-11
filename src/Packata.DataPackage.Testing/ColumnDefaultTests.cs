using DbType = System.Data.DbType;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;
using Packata.DataPackage.Mapping;
using Packata.DataPackage.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Packata.DataPackage.Testing;

public class ColumnDefaultTests
{
    [TestCase(SerializationFormat.Json)]
    [TestCase(SerializationFormat.Yaml)]
    public void Round_trips_a_quoted_literal(SerializationFormat format)
    {
        var document = format == SerializationFormat.Json
            ? JsonDescriptor("""{"type":"boolean","value":"false"}""")
            : """
                resources:
                  - name: sample
                    type: table
                    data: []
                    schema:
                      fields:
                        - name: flag
                          type: boolean
                          packata:customProperties:
                            - property: defaultValue
                              value: {type: boolean, value: 'false'}
                """;
        var package = Load(document, format);
        var output = format == SerializationFormat.Json
            ? JsonConvert.SerializeObject(package, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore
            })
            : new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithQuotingNecessaryStrings().ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .Build().Serialize(package);
        var first = package.ToCanonicalContract().RequireValue().Assets.Single().Schema!.Fields.Single();
        var second = Load(output, format).ToCanonicalContract().RequireValue().Assets.Single().Schema!.Fields.Single();
        Assert.That(first.DefaultValue?.Type, Is.EqualTo(DbType.Boolean));
        Assert.That(first.DefaultValue?.Value, Is.False);
        Assert.That(second.DefaultValue, Is.EqualTo(first.DefaultValue));
    }

    [TestCase("null")]
    [TestCase("[]")]
    [TestCase("1")]
    [TestCase("""{"type":null,"value":"x"}""")]
    [TestCase("""{"type":"string","value":null}""")]
    [TestCase("""{"type":"boolean","value":false}""")]
    [TestCase("""{"type":"int16","value":"32768"}""")]
    [TestCase("""{"type":"decimal","value":"0.10000000000000000000000000001"}""")]
    [TestCase("""{"type":"single","value":"1e50"}""")]
    [TestCase("""{"type":"double","value":"NaN"}""")]
    [TestCase("""{"type":"datetime","value":"2026-11-11T01:02:03Z"}""")]
    [TestCase("""{"type":"unknown","value":"x"}""")]
    public void Rejects_invalid_literals(string value)
    {
        var result = Load(JsonDescriptor(value), SerializationFormat.Json).ToCanonicalContract();
        Assert.That(result.IsSuccessful, Is.False);
        Assert.That(result.Diagnostics.Single(diagnostic => diagnostic.Code == "DEFAULT001").SourcePath, Does.Contain("flag"));
    }

    private static string JsonDescriptor(string value) => $$$"""
        {"resources":[{"name":"sample","type":"table","data":[],"schema":{"fields":[
          {"name":"flag","type":"boolean","packata:customProperties":[{"property":"defaultValue","value":{{{value}}} }]}
        ]}}]}
        """;

    private static DataPackage Load(string input, SerializationFormat format)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        return new DataPackageFactory().LoadFromStream(stream, format);
    }
}
