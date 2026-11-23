using System.Data;
using System.Text;
using System.Text.Json;
using Chrononuensis;
using NetTopologySuite.Geometries;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.ExtendedTypes;

namespace Packata.ResourceReaders.Testing;

public class ExtendedTypesReaderTests
{
    private static readonly DataEndpoint Csv = new("data", "data", EndpointKind.File, null, new PathLocation(["data.csv"]));

    [Test]
    public async Task Activate_both_profiles_on_one_factory_without_affecting_other_factories()
    {
        var schema = new DataSchema(
        [
            new("year", "year"),
            new("period", "yearmonth"),
            new("geojson", "geojson"),
            new("location", "geopoint", Format: "array"),
            new("count", "integer", Format: "i64")
        ]);
        var input = "year,period,geojson,location,count\r\n"
            + "2026,2026-11,\"{\"\"type\"\":\"\"Point\"\",\"\"coordinates\"\":[4,50]}\",\"[4,50]\",3000000000\r\n";
        var streams = new CsvStream(input);
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.UseNetTopologySuite();
            options.AddDelimited();
            options.UseChrononuensis();
        }, streams);

        using var extended = await factory.OpenAsync(Csv, schema);
        using var defaults = await new ResourceReaderFactory(streams).OpenAsync(Csv, schema);

        Assert.That(extended.Read(), Is.True);
        Assert.That(defaults.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            AssertField(extended, 0, new Year(2026));
            AssertField(extended, 1, new YearMonth(2026, 11));
            Assert.That(extended.GetFieldType(2), Is.EqualTo(typeof(Geometry)));
            Assert.That(extended.GetValue(2), Is.TypeOf<Point>());
            Assert.That(extended.GetFieldType(3), Is.EqualTo(typeof(Point)));
            Assert.That(((Point)extended.GetValue(3)).Coordinate, Is.EqualTo(new Coordinate(4, 50)));
            AssertField(extended, 4, 3000000000L);

            AssertField(defaults, 0, 2026);
            AssertField(defaults, 1, "2026-11");
            Assert.That(defaults.GetFieldType(2), Is.EqualTo(typeof(JsonElement)));
            AssertField(defaults, 3, new GeoPoint(4, 50));
        }
    }

    [Test]
    public async Task Read_explicit_temporal_format_with_native_parser()
    {
        var factory = ResourceReaderFactory.Create(options => options.UseChrononuensis(),
            new CsvStream("period\r\n11-2026\r\n"));
        var schema = new DataSchema([new("period", "yearmonth", Format: "%m-%Y", Language: "en-US")]);

        using var reader = await factory.OpenAsync(Csv, schema);

        Assert.That(reader.Read(), Is.True);
        AssertField(reader, 0, new YearMonth(2026, 11));
    }

    [Test]
    public async Task Read_WKT_subtype_and_preserve_TopoJSON_as_JSON()
    {
        var factory = ResourceReaderFactory.Create(options => options.UseNetTopologySuite(),
            new CsvStream("shape,topology\r\nPOINT (4 50),\"{\"\"type\"\":\"\"Topology\"\"}\"\r\n"));
        var schema = new DataSchema(
        [
            new DataField("shape", "geometry")
            {
                Spatial = new(SpatialModel.Geometry, SpatialSubtype.Point, "EPSG:3857", Encoding: SpatialEncoding.Wkt)
            },
            new("topology", "geojson", Format: "topojson")
        ]);

        using var reader = await factory.OpenAsync(Csv, schema);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(Point)));
        Assert.That(((Point)reader.GetValue(0)).Coordinate, Is.EqualTo(new Coordinate(4, 50)));
        Assert.That(reader.GetFieldType(1), Is.EqualTo(typeof(JsonElement)));
        Assert.That(((JsonElement)reader.GetValue(1)).GetProperty("type").GetString(), Is.EqualTo("Topology"));

        var noProfile = new ResourceReaderFactory(new CsvStream("shape,topology\r\nPOINT (4 50),{}\r\n"));
        using var textReader = await noProfile.OpenAsync(Csv, schema);
        Assert.That(textReader.GetFieldType(0), Is.EqualTo(typeof(string)));
        Assert.That(textReader.Read(), Is.True);
        Assert.That(textReader.GetFieldValue<string>(0), Is.EqualTo("POINT (4 50)"));
    }

    [TestCase("Feature")]
    [TestCase("FeatureCollection")]
    public async Task Reject_GeoJSON_values_that_do_not_represent_a_geometry(string kind)
    {
        var content = $"shape\r\n\"{{\"\"type\"\":\"\"{kind}\"\"}}\"\r\n";
        var fallbackCalls = 0;
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.UseNetTopologySuite();
            options.AddValueConverter<JsonElement, Geometry>(_ =>
            {
                fallbackCalls++;
                return new Point(0, 0);
            }, logicalType: "geojson");
        }, new CsvStream(content));
        using var reader = await factory.OpenAsync(Csv, new DataSchema([new("shape", "geojson")]));

        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.GetValue(0), Throws.InstanceOf<FormatException>());
        Assert.That(fallbackCalls, Is.Zero);
    }

    [Test]
    public void Report_an_unavailable_binding_on_the_inline_reader()
    {
        var factory = ResourceReaderFactory.Create(options => options.UseChrononuensis());
        var endpoint = new DataEndpoint("data", "data", EndpointKind.Inline, null,
            new InlineLocation(new[] { new object[] { "2026" } }));
        var schema = new DataSchema([new("year", "year")]);

        Assert.That(async () => await factory.OpenAsync(endpoint, schema),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("year").And.Message.Contains("Chrononuensis"));
    }

    private static void AssertField<T>(IDataReader reader, int index, T value)
    {
        Assert.That(reader.GetFieldType(index), Is.EqualTo(typeof(T)));
        Assert.That(reader.GetValue(index), Is.EqualTo(value));
    }

    private sealed class CsvStream(string content) : IEndpointStreamResolver
    {
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false));
    }
}
