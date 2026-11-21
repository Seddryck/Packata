using DbType = System.Data.DbType;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;
using Packata.Core.Contracts;
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
        var output = Serialize(package, format);
        var first = package.ToCanonicalContract().RequireValue().Assets.Single().Schema!.Fields.Single();
        var second = Load(output, format).ToCanonicalContract().RequireValue().Assets.Single().Schema!.Fields.Single();
        Assert.That(first.DefaultValue?.Type, Is.EqualTo(DbType.Boolean));
        Assert.That(first.DefaultValue?.Value, Is.False);
        Assert.That(second.DefaultValue, Is.EqualTo(first.DefaultValue));
    }

    private static string Serialize(DataPackage package, SerializationFormat format)
        => format == SerializationFormat.Json
            ? JsonConvert.SerializeObject(package, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore
            })
            : new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithQuotingNecessaryStrings().ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .Build().Serialize(package);
    [TestCase("""{"kind":"current-timestamp"}""", ColumnDefaultKind.CurrentTimestamp, null)]
    [TestCase("""{"kind":"current-utc-timestamp","fractionalSecondsPrecision":0}""", ColumnDefaultKind.CurrentUtcTimestamp, 0)]
    [TestCase("""{"kind":"current-date"}""", ColumnDefaultKind.CurrentDate, null)]
    [TestCase("""{"kind":"current-time","fractionalSecondsPrecision":9}""", ColumnDefaultKind.CurrentTime, 9)]
    [TestCase("""{"kind":"next-sequence-value","sequence":{"schema":"Billing Schema","name":"Invoice Numbers"}}""",
        ColumnDefaultKind.NextSequenceValue, null)]
    [TestCase("""{"kind":"current-user"}""", ColumnDefaultKind.CurrentUser, null)]
    [TestCase("""{"kind":"session-user"}""", ColumnDefaultKind.SessionUser, null)]
    [TestCase("""{"kind":"new-uuid","representation":"canonical-text"}""", ColumnDefaultKind.NewUuid, null)]
    public void Round_trips_default_expressions(string declaration, ColumnDefaultKind kind, int? precision)
    {
        var json = JsonDescriptor(declaration).Replace("defaultValue", "defaultExpression", StringComparison.Ordinal);
        var jsonPackage = Load(json, SerializationFormat.Json);
        var expected = new ColumnDefaultExpression(kind, precision, kind == ColumnDefaultKind.NextSequenceValue
            ? new("Invoice Numbers", Schema: "Billing Schema") : null);
        var yaml = $$"""
            resources:
              - name: sample
                type: table
                data: []
                schema:
                  fields:
                    - name: flag
                      type: boolean
                      packata:customProperties:
                        - property: defaultExpression
                          value: {{declaration}}
            """;
        var yamlPackage = Load(yaml, SerializationFormat.Yaml);
        Assert.That(ReadExpression(jsonPackage), Is.EqualTo(expected));
        Assert.That(ReadExpression(yamlPackage), Is.EqualTo(expected));
        Assert.That(ReadExpression(Load(Serialize(jsonPackage, SerializationFormat.Json), SerializationFormat.Json)), Is.EqualTo(expected));
        Assert.That(ReadExpression(Load(Serialize(yamlPackage, SerializationFormat.Yaml), SerializationFormat.Yaml)), Is.EqualTo(expected));
    }

    [TestCase("""{"kind":"current-time","fractionalSecondsPrecision":10}""")]
    [TestCase("""{"kind":"current-time","fractionalSecondsPrecision":"3"}""")]
    [TestCase("""{"kind":"current-date","fractionalSecondsPrecision":0}""")]
    [TestCase("""{"kind":"current-timestamp","sql":"now()"}""")]
    [TestCase("""{"kind":"CURRENT-DATE"}""")]
    [TestCase("\"CURRENT_TIMESTAMP\"")]
    [TestCase("""{"kind":"next-sequence-value","sequence":{"name":"seq"}}""")]
    [TestCase("""{"kind":"next-sequence-value","sequence":{"name":"seq","schema":""}}""")]
    [TestCase("""{"kind":"next-sequence-value","sequence":{"name":"seq","schema":"a\u0000b"}}""")]
    [TestCase("""{"kind":"next-sequence-value","sequence":{"name":"seq","schema":"billing","other":"extra"}}""")]
    [TestCase("""{"kind":"next-sequence-value","sequence":"billing.seq"}""")]
    [TestCase("""{"kind":"new-uuid"}""")]
    [TestCase("""{"kind":"new-uuid","representation":"native"}""")]
    [TestCase("""{"kind":"new-uuid","representation":"canonical-text","byteOrder":"network"}""")]
    [TestCase("""{"kind":"new-uuid","representation":"canonical-text","fractionalSecondsPrecision":0}""")]
    [TestCase("""{"kind":"current-user","fractionalSecondsPrecision":0}""")]
    [TestCase("""{"kind":"session-user","representation":"canonical-text"}""")]
    public void Rejects_invalid_expressions(string declaration)
    {
        var json = JsonDescriptor(declaration).Replace("defaultValue", "defaultExpression", StringComparison.Ordinal);
        var mapping = Load(json, SerializationFormat.Json).ToCanonicalContract();
        Assert.That(mapping.Diagnostics.Single(diagnostic => diagnostic.Code == "DEFAULT002").SourcePath, Does.Contain("flag"));
    }

    [TestCase("defaultValue")]
    [TestCase("defaultExpression")]
    public void Rejects_duplicate_or_conflicting_defaults(string secondProperty)
    {
        var json = JsonDescriptor("""{"type":"boolean","value":"false"}""")
            .Replace(" }]}", $$""" }, {"property":"{{secondProperty}}","value":null}]}""", StringComparison.Ordinal);
        var mapping = Load(json, SerializationFormat.Json).ToCanonicalContract();
        Assert.That(mapping.Diagnostics.Single(diagnostic => diagnostic.Code == "DEFAULT001").SourcePath, Does.Contain("flag"));
    }

    private static ColumnDefaultExpression? ReadExpression(DataPackage package)
        => package.ToCanonicalContract().RequireValue().Assets.Single().Schema!.Fields.Single().DefaultExpression;

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

    [Test]
    public void Replaces_only_the_selected_default_property_inside_the_existing_container()
    {
        var json = JsonDescriptor("""{"type":"boolean","value":"true"}""")
            .Replace(" }]}", """ }, {"property":"initial","value":{"type":"boolean","value":"false"}}]}""", StringComparison.Ordinal);
        var package = Load(json, SerializationFormat.Json);
        var options = new DataPackageMappingOptions { DefaultValue = DataPackageBindingOverride.FromProperty("initial") };
        Assert.That(package.ToCanonicalContract().RequireValue().Assets[0].Schema!.Fields[0].DefaultValue!.Value, Is.True);
        var field = package.ToCanonicalContract(options).RequireValue().Assets[0].Schema!.Fields[0];
        Assert.That(field.DefaultValue!.Value, Is.False);
        Assert.That(package.Resources[0].Schema!.Fields[0].CustomProperties!.Select(property => property.Property),
            Is.EqualTo(new[] { "defaultValue", "initial" }));
        Assert.That(() => new DataPackageMapper(new() { DefaultValue = DataPackageBindingOverride.FromProperty("defaultExpression") }),
            Throws.ArgumentException.With.Message.Contains("field.defaultExpression"));
    }

    [Test]
    public void Presets_are_composable_and_keep_native_schema_settings_and_metadata()
    {
        var json = JsonDescriptor("""{"type":"boolean","value":"true"}""")
            .Replace("\"schema\":{", "\"schema\":{\"fieldsMatch\":\"exact\",", StringComparison.Ordinal)
            .Replace(" }]}", """ }, {"property":"initial","value":{"type":"boolean","value":"false"}}]}""", StringComparison.Ordinal);
        var package = Load(json, SerializationFormat.Json);
        var defaults = DataPackageMappingOptions.Default;
        var none = DataPackageMappingOptions.NoCustomPropertyBindings;
        var withoutDefault = defaults with { DefaultValue = DataPackageBindingOverride.Disabled };
        var withBuiltIn = none with { DefaultValue = DataPackageBindingOverride.BuiltIn };
        var withReplacement = none with { DefaultValue = DataPackageBindingOverride.FromProperty("initial") };
        var implicitSchema = package.ToCanonicalContract().RequireValue().Assets[0].Schema!;
        Assert.That(implicitSchema.Fields[0].DefaultValue!.Value, Is.True);

        foreach (var (options, expected) in new (DataPackageMappingOptions, bool?)[]
        {
            (defaults, true), (none, null), (withoutDefault, null), (withBuiltIn, true), (withReplacement, false),
        })
        {
            var schema = package.ToCanonicalContract(options).RequireValue().Assets[0].Schema!;
            var field = schema.Fields[0];
            Assert.That(field.DefaultValue?.Value, Is.EqualTo(expected));
            Assert.That(field.DefaultExpression, Is.Null);
            Assert.That(field.Required, Is.True);
            Assert.That(schema.EnforcePropertyOrder, Is.True);
            Assert.That(field.Extensions["datapackage"]["customProperties"], Is.Not.Empty);
        }
    }

    [Test]
    public void Disabled_defaults_do_not_validate_or_collide_with_enabled_selectors()
    {
        var json = JsonDescriptor("null")
            .Replace("\"resources\":", "\"name\":\"sample\",\"resources\":", StringComparison.Ordinal)
            .Replace(" }]}", """ }, {"property":"defaultExpression","value":{"type":"boolean","value":"false"}}]}""", StringComparison.Ordinal);
        var package = Load(json, SerializationFormat.Json);
        Assert.That(package.ToCanonicalContract().Diagnostics.Single().Code, Is.EqualTo("DEFAULT001"));
        Assert.That(package.ToCanonicalContract(DataPackageMappingOptions.NoCustomPropertyBindings).Diagnostics, Is.Empty);

        var options = DataPackageMappingOptions.Default with
        {
            DefaultValue = DataPackageBindingOverride.FromProperty("defaultExpression"),
            DefaultExpression = DataPackageBindingOverride.Disabled,
        };
        var field = package.ToCanonicalContract(options).RequireValue().Assets[0].Schema!.Fields[0];
        Assert.That(field.DefaultValue!.Value, Is.False);
        Assert.That(field.DefaultExpression, Is.Null);
        Assert.That(() => DataPackageBindingOverride.FromProperty(" "), Throws.ArgumentException);
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
