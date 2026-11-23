using System.Data;
using System.Text;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ExtendedTypes;

namespace Packata.ResourceReaders.Testing;

public class GeoPointReaderBindingTests
{
    private static readonly DataEndpoint Endpoint =
        new("data", "data", EndpointKind.File, null, new PathLocation(["points.csv"]));
    private static readonly DataSchema Schema = new(
    [
        new DataField("location", "geopoint", Format: "array", Extensions: ExtensionMetadata.For(
            "datapackage", new Dictionary<string, object?> { ["missingValues"] = new[] { "NA" } }))
    ]);

    [Test]
    public async Task Native_point_binding_wins_and_malformed_values_do_not_trigger_fallback()
    {
        var conversions = 0;
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.UseNetTopologySuite();
            options.AddValueConverter<string, Point>(text =>
            {
                conversions++;
                return ToPoint(text);
            }, "geopoint", "array");
        }, new CsvStream("location\r\n\"[4.35,50.85]\"\r\n[4.35]\r\n"));

        using var reader = await factory.OpenAsync(Endpoint, Schema);

        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(Point)));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<Point>(0).Coordinate, Is.EqualTo(new Coordinate(4.35, 50.85)));
        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.GetValue(0), Throws.TypeOf<FormatException>());
        Assert.That(conversions, Is.Zero);
    }

    [Test]
    public async Task Configured_geopoint_fallback_respects_missing_values_and_retains_the_point()
    {
        var conversions = 0;
        var factory = ResourceReaderFactory.Create(options =>
        {
            SelectPoint(options);
            options.AddValueConverter<string, Point>(text =>
            {
                conversions++;
                return ToPoint(text);
            }, "geopoint", "array");
        }, new CsvStream("location\r\n\"NA\"\r\n\"[4.35,50.85]\"\r\n\"[10,20]\"\r\n"));
        Point first;

        using (var reader = await factory.OpenAsync(Endpoint, Schema))
        {
            Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(Point)));
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.IsDBNull(0), Is.True);
            Assert.That(reader.GetValue(0), Is.EqualTo(DBNull.Value));
            Assert.That(conversions, Is.Zero);

            Assert.That(reader.Read(), Is.True);
            first = reader.GetFieldValue<Point>(0);
            Assert.That(reader.GetValue(0), Is.SameAs(first));
            Assert.That(conversions, Is.EqualTo(1));

            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetFieldValue<Point>(0).Coordinate, Is.EqualTo(new Coordinate(10, 20)));
        }

        Assert.That(first.Coordinate, Is.EqualTo(new Coordinate(4.35, 50.85)));
    }

    [Test]
    public void Csv_binding_rejects_a_geopoint_converter_with_incompatible_output()
    {
        var factory = ResourceReaderFactory.Create(options =>
        {
            SelectPoint(options);
            options.AddValueConverter<string, GeoPoint>(text => GeoPointParser.ParseArray(text),
                "geopoint", "array", targetType: typeof(Point));
        }, new CsvStream(string.Empty));

        Assert.That(async () => await factory.OpenAsync(Endpoint, Schema),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("location")
                .And.Message.Contains(typeof(GeoPoint).FullName!).And.Message.Contains(typeof(Point).FullName!));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Csv_binding_reports_when_no_geopoint_binding_is_available(bool configureUnsupportedSource)
    {
        var factory = ResourceReaderFactory.Create(options =>
        {
            SelectPoint(options);
            if (configureUnsupportedSource)
            {
                options.AddValueConverter<JsonElement, Point>(json => ToPoint(json.GetRawText()), "geopoint", "array");
            }
        }, new CsvStream(string.Empty));

        Assert.That(async () => await factory.OpenAsync(Endpoint, Schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains(DelimitedFieldBinding.ReaderKey)
                .And.Message.Contains("location").And.Message.Contains(typeof(Point).FullName!));
    }

    private static void SelectPoint(ResourceReaderFactoryOptions options)
        => options.AddRepresentationProfile("TestPoint", field => field.LogicalType == "geopoint" ? typeof(Point) : null);

    private static Point ToPoint(string text)
    {
        var point = GeoPointParser.ParseArray(text);
        return new Point(point.Longitude, point.Latitude);
    }

    private sealed class CsvStream(string content) : IEndpointStreamResolver
    {
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false));
    }
}
