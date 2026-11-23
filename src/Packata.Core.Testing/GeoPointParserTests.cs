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
    public void Parse_and_TryParse_select_the_requested_format(string text, GeoPointFormat format)
    {
        var expected = new GeoPoint(4.35, 50.85);
        Assert.That(GeoPointParser.Parse(text, format), Is.EqualTo(expected));
        Assert.That(GeoPointParser.TryParse(text, format, out var result), Is.True);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("4.35, invalid", GeoPointFormat.Default)]
    [TestCase("4.35, NaN", GeoPointFormat.Default)]
    [TestCase("[4.35,", GeoPointFormat.Array)]
    [TestCase("[4.35]", GeoPointFormat.Array)]
    [TestCase("{\"lon\":4.35}", GeoPointFormat.Object)]
    [TestCase("{", GeoPointFormat.Auto)]
    [TestCase("[4.35,50.85]", GeoPointFormat.Object)]
    [TestCase("4.35,50.85", (GeoPointFormat)99)]
    public void TryParse_returns_false_for_invalid_values_and_formats(string text, GeoPointFormat format)
    {
        Assert.That(GeoPointParser.TryParse(text, format, out var result), Is.False);
        Assert.That(result, Is.EqualTo(default(GeoPoint)));
    }

    [Test]
    public void TryParse_returns_false_for_invalid_utf16_in_json()
        => Assert.That(GeoPointParser.TryParse("[\uD800,50.85]", GeoPointFormat.Array, out _), Is.False);

    [Test]
    public void Parse_without_a_format_expects_a_coordinate_pair()
    {
        Assert.That(GeoPointParser.Parse("4.35, 50.85"), Is.EqualTo(new GeoPoint(4.35, 50.85)));
        Assert.That(GeoPointParser.TryParse("4.35, 50.85", out var result), Is.True);
        Assert.That(result, Is.EqualTo(new GeoPoint(4.35, 50.85)));
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
