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
