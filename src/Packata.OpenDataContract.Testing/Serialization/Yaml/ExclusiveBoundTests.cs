using NUnit.Framework;
using Packata.OpenDataContract.Types;

namespace Packata.OpenDataContract.Testing.Serialization.Yaml;

public sealed class ExclusiveBoundTests
{
    [TestCase("0", "1", "1", true)]
    [TestCase("2", "1", "2", false)]
    [TestCase("1", "1", "1", true)]
    public void NumericExclusiveMinimum_RetainsTheTightestBound(string inclusive, string exclusive, string expected, bool exclusiveExpected)
    {
        var type = new NumberLogicalType(new() { ["minimum"] = inclusive, ["exclusiveMinimum"] = exclusive });
        Assert.That(type.Minimum, Is.EqualTo(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(type.ExclusiveMinimum, Is.EqualTo(exclusiveExpected));
    }

    [Test]
    public void TemporalExclusiveMaximum_IsAValueRatherThanABoolean()
    {
        var type = new DateLogicalType(new() { ["maximum"] = "2026-10-30", ["exclusiveMaximum"] = "2026-10-01" });
        Assert.That(type.Maximum, Is.EqualTo(new DateTime(2026, 10, 1)));
        Assert.That(type.ExclusiveMaximum, Is.True);
    }
}
