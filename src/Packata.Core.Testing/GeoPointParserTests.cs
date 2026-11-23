using NUnit.Framework;

namespace Packata.Core.Testing;

public class GeoPointParserTests
{
    [TestCase("4.35, 50.85", GeoPointFormat.Default)]
    [TestCase("[4.35, 50.85]", GeoPointFormat.Array)]
    [TestCase("{\"lat\":50.85,\"lon\":4.35}", GeoPointFormat.Object)]
    [TestCase("  4.35, 50.85", GeoPointFormat.Auto)]
    [TestCase("  [4.35, 50.85]", GeoPointFormat.Auto)]
    [TestCase("  {\"lat\":50.85,\"lon\":4.35}", GeoPointFormat.Auto)]
    public void Parse_selects_the_requested_format(string text, GeoPointFormat format)
        => Assert.That(GeoPointParser.Parse(text, format), Is.EqualTo(new GeoPoint(4.35, 50.85)));

    [Test]
    public void Parse_without_a_format_expects_a_coordinate_pair()
    {
        Assert.That(GeoPointParser.Parse("4.35, 50.85"), Is.EqualTo(new GeoPoint(4.35, 50.85)));
        Assert.That(() => GeoPointParser.Parse("[4.35, 50.85]"), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Parse_rejects_a_value_in_a_different_format()
        => Assert.That(() => GeoPointParser.Parse("[4.35, 50.85]", GeoPointFormat.Object),
            Throws.TypeOf<FormatException>());

    [Test]
    public void Parse_rejects_an_undefined_format()
        => Assert.That(() => GeoPointParser.Parse("4.35, 50.85", (GeoPointFormat)99),
            Throws.TypeOf<ArgumentOutOfRangeException>());
}
