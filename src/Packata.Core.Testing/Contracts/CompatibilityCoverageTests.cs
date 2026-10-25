using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Core.Testing.Contracts;

public class CompatibilityCoverageTests
{
    [TestCase("required", "invalid")]
    [TestCase("minLength", "-1")]
    [TestCase("minimum", "invalid")]
    public void UnsupportedConstraintValues_RetainTheirFullSourcePaths(string kind, string value)
    {
        var descriptor = Package(new Dictionary<string, object?> { [kind] = value });

        Assert.That(CompatibilityCoverage.Unsupported(descriptor, ContractStandard.DataPackage),
            Is.EqualTo(new[] { "$.resources[0].schema.fields[0].constraints." + kind }));
    }

    [Test]
    public void SupportedConstraintValues_AreCovered()
    {
        var descriptor = Package(new Dictionary<string, object?>
        {
            ["required"] = "true", ["minLength"] = "0", ["minimum"] = "1.5", ["enum"] = new object?[] { "a", "b" }
        });

        Assert.That(CompatibilityCoverage.Unsupported(descriptor, ContractStandard.DataPackage), Is.Empty);
    }

    [Test]
    public void ObjectsInScalarPositions_AreReported()
    {
        var descriptor = new Dictionary<string, object?>
        {
            ["name"] = new Dictionary<string, object?>(),
            ["keywords"] = new object?[] { "valid", new Dictionary<string, object?>() }
        };

        Assert.That(CompatibilityCoverage.Unsupported(descriptor, ContractStandard.DataPackage),
            Is.EqualTo(new[] { "$.keywords", "$.name" }));
    }

    [Test]
    public void InvalidArrayAndObjectShapes_AreReportedAtTheirSourceLocations()
    {
        var descriptor = new Dictionary<string, object?> { ["resources"] = "not an array" };
        Assert.That(CompatibilityCoverage.Unsupported(descriptor, ContractStandard.DataPackage),
            Is.EqualTo(new[] { "$.resources" }));

        descriptor["resources"] = new object?[] { "not an object" };
        Assert.That(CompatibilityCoverage.Unsupported(descriptor, ContractStandard.DataPackage),
            Is.EqualTo(new[] { "$.resources[0]" }));
    }

    [Test]
    public void OdcsNestedFields_UseTheirOwnLogicalTypeForOptionCoverage()
    {
        var descriptor = new Dictionary<string, object?>
        {
            ["schema"] = new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["properties"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["logicalType"] = "array",
                            ["logicalTypeOptions"] = new Dictionary<string, object?> { ["uniqueItems"] = "true" },
                            ["items"] = new Dictionary<string, object?>
                            {
                                ["logicalType"] = "integer",
                                ["logicalTypeOptions"] = new Dictionary<string, object?> { ["minimum"] = "1.5" },
                                ["unmapped"] = "value"
                            }
                        }
                    }
                }
            }
        };

        Assert.That(CompatibilityCoverage.Unsupported(descriptor, ContractStandard.Odcs), Is.EqualTo(new[]
        {
            "$.schema[0].properties[0].items.logicalTypeOptions.minimum",
            "$.schema[0].properties[0].items.unmapped"
        }));
    }

    [Test]
    public void OdcsDescriptor_CoversMetadataServersNestedFieldsAndEnums()
    {
        var descriptor = Map(
            ("apiVersion", "v3.1.0"), ("kind", "DataContract"), ("id", "sales"), ("name", "Sales"),
            ("version", "1.0.0"), ("status", "active"), ("domain", "commerce"), ("tags", new[] { "orders" }),
            ("description", Map(("purpose", "Sales data"), ("usage", "Analytics"), ("limitations", "Daily updates"))),
            ("servers", new[] { Map(("server", "warehouse"), ("type", "postgres"), ("host", "localhost"),
                ("port", "5432"), ("database", "sales"), ("schema", "public"), ("catalog", "sales"),
                ("description", "Warehouse"), ("environment", "test"), ("path", "orders.csv"),
                ("location", "local"), ("format", "csv"), ("encoding", "utf-8")) }),
            ("schema", new[] { Map(("name", "orders"), ("physicalName", "orders_v1"),
                ("physicalType", "table"), ("description", "Orders"), ("properties", new[]
                {
                    Map(("name", "status"), ("logicalType", "string"), ("physicalName", "order_status"),
                        ("physicalType", "varchar"), ("description", "Status"), ("required", "true"),
                        ("nullable", "false"), ("unique", "false"), ("primaryKey", "false"), ("primaryKeyPosition", "0"),
                        ("logicalTypeOptions", Map(("format", "default"), ("minLength", "1"), ("maxLength", "20"), ("pattern", "^[a-z]+$"))),
                        ("enum", new[] { Map(("value", "open")), Map(("value", "closed")) })),
                    Map(("name", "details"), ("logicalType", "object"),
                        ("properties", new[] { Map(("name", "active"), ("logicalType", "boolean")) })),
                    Map(("name", "amounts"), ("logicalType", "array"),
                        ("items", Map(("name", "amount"), ("logicalType", "number"))))
                })) }));

        Assert.That(CompatibilityCoverage.Unsupported(descriptor, ContractStandard.Odcs), Is.Empty);
    }

    [Test]
    public void DataPackageDescriptor_CoversResourceSchemaAndDialectMetadata()
    {
        var descriptor = Map(("name", "sales"), ("id", "sales-v1"), ("title", "Sales"),
            ("description", "Sales data"), ("version", "1.0.0"), ("homepage", "https://example.com"),
            ("profile", "data-package"), ("created", "2026-01-01"), ("keywords", new[] { "sales" }),
            ("resources", new[] { Map(("name", "orders"), ("title", "Orders"), ("description", "Daily orders"),
                ("path", "orders.csv"), ("format", "csv"), ("mediatype", "text/csv"), ("encoding", "utf-8"),
                ("compression", "none"), ("type", "table"), ("kind", "table"), ("profile", "tabular-data-resource"),
                ("homepage", "https://example.com/orders"), ("bytes", "100"), ("hash", "abc"),
                ("schema", Map(("primaryKey", new[] { "id" }), ("fields", new[]
                {
                    Map(("name", "id"), ("type", "integer"), ("format", "default"), ("title", "Identifier"),
                        ("description", "Order id"), ("example", "1"), ("rdfType", "identifier"),
                        ("x-packata-physicalName", "order_id"), ("x-packata-physicalType", "int"),
                        ("constraints", Map(("required", "true"), ("unique", "true"), ("minimum", "0"),
                            ("maximum", "100"), ("exclusiveMinimum", "-1"), ("exclusiveMaximum", "101"),
                            ("minLength", "0"), ("maxLength", "10"), ("pattern", "[0-9]+"), ("enum", new[] { "1", "2" }))))
                }))),
                ("dialect", Map(("header", "true"), ("headerRows", new[] { 1 }), ("headerJoin", " "),
                    ("commentRows", new[] { 0 }), ("commentChar", "#"), ("delimiter", ","),
                    ("lineTerminator", "\n"), ("quoteChar", "\""), ("doubleQuote", "true"),
                    ("escapeChar", "\\"), ("skipInitialSpace", "false")))) }));

        Assert.That(CompatibilityCoverage.Unsupported(descriptor, ContractStandard.DataPackage), Is.Empty);
    }

    [TestCase("integer", "minimum", "-1", true)]
    [TestCase("integer", "maximum", "1.5", false)]
    [TestCase("number", "minimum", "1.5", true)]
    [TestCase("decimal", "maximum", "invalid", false)]
    [TestCase("number", "exclusiveMinimum", "true", true)]
    [TestCase("number", "exclusiveMaximum", "invalid", false)]
    [TestCase("array", "minItems", "0", true)]
    [TestCase("array", "maxItems", "-1", false)]
    [TestCase("array", "uniqueItems", "false", true)]
    [TestCase("object", "minProperties", "1", true)]
    [TestCase("object", "maxProperties", "invalid", false)]
    [TestCase("object", "required", "id", true)]
    [TestCase("date", "format", "yyyy-MM-dd", true)]
    [TestCase("time", "format", "HH:mm:ss", true)]
    [TestCase("timestamp", "format", "default", true)]
    [TestCase("datetime", "format", "default", true)]
    [TestCase("date", "minimum", "2026-01-01", false)]
    [TestCase("custom", "format", "default", false)]
    [SetCulture("en-US")]
    public void OdcsOptions_RespectLogicalTypesAndNativeValueRepresentations(string type, string key, string value, bool supported)
    {
        var descriptor = OdcsField(Map(("logicalType", type), ("logicalTypeOptions", Map((key, value)))));
        var paths = CompatibilityCoverage.Unsupported(descriptor, ContractStandard.Odcs);

        Assert.That(paths, Is.EqualTo(supported ? Array.Empty<string>() :
            new[] { "$.schema[0].properties[0].logicalTypeOptions." + key }));
    }

    [TestCase("en-US", true)]
    [TestCase("fr-FR", false)]
    public void OdcsDecimalBounds_RejectValuesTheNativeCultureWouldMisread(string culture, bool supported)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var descriptor = OdcsField(Map(("logicalType", "number"), ("logicalTypeOptions", Map(("minimum", "1.5")))));
            Assert.That(CompatibilityCoverage.Unsupported(descriptor, ContractStandard.Odcs).Any(), Is.EqualTo(!supported));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Test]
    public void UnmappedMembers_AreReportedInEachNativeDescriptorContext()
    {
        var descriptor = Map(("unmapped", "root"), ("description", Map(("unmapped", "description"))),
            ("servers", new[] { Map(("unmapped", "server")) }),
            ("schema", new[] { Map(("unmapped", "dataset"), ("properties", new[]
            {
                Map(("unmapped", "field"), ("enum", new[] { Map(("unmapped", "enum")) }))
            })) }));
        Assert.That(CompatibilityCoverage.Unsupported(descriptor, ContractStandard.Odcs), Is.EqualTo(new[]
        {
            "$.description.unmapped", "$.schema[0].properties[0].enum[0].unmapped",
            "$.schema[0].properties[0].unmapped", "$.schema[0].unmapped", "$.servers[0].unmapped", "$.unmapped"
        }));

        var package = Map(("unmapped", "root"), ("resources", new[]
        {
            Map(("unmapped", "resource"), ("dialect", Map(("unmapped", "dialect"))),
                ("schema", Map(("unmapped", "schema"), ("fields", new[]
                {
                    Map(("unmapped", "field"), ("constraints", Map(("unmapped", "constraint"))))
                }))))
        }));
        Assert.That(CompatibilityCoverage.Unsupported(package, ContractStandard.DataPackage), Is.EqualTo(new[]
        {
            "$.resources[0].dialect.unmapped", "$.resources[0].schema.fields[0].constraints.unmapped",
            "$.resources[0].schema.fields[0].unmapped", "$.resources[0].schema.unmapped", "$.resources[0].unmapped", "$.unmapped"
        }));
    }

    private static Dictionary<string, object?> Map(params (string Key, object? Value)[] entries)
        => entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    private static Dictionary<string, object?> OdcsField(Dictionary<string, object?> field)
        => Map(("schema", new[] { Map(("properties", new[] { field })) }));

    private static Dictionary<string, object?> Package(Dictionary<string, object?> constraints) => new()
    {
        ["resources"] = new object?[]
        {
            new Dictionary<string, object?>
            {
                ["schema"] = new Dictionary<string, object?>
                {
                    ["fields"] = new object?[] { new Dictionary<string, object?> { ["constraints"] = constraints } }
                }
            }
        }
    };
}
