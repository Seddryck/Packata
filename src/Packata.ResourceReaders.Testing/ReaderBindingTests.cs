using System.Data;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Testing;

public class ReaderBindingTests
{
    private static readonly DataEndpoint Inline = new("test", "test", EndpointKind.Inline, null, new InlineLocation(Array.Empty<object>()));
    private static readonly DataEndpoint Csv = new("test", "test", EndpointKind.File, null, new PathLocation(["values.csv"]));
    private static readonly DataSchema LongField = new([new("value", "integer", Format: "i64")]);

    [Test]
    public async Task Native_binding_wins_without_running_the_converter()
    {
        var converted = 0;
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.AddProvider(new ValueReader(typeof(long), 8L, typeof(int)));
            options.AddValueConverter<int, long>(value => { converted++; return value; });
        });

        using var reader = await factory.OpenAsync(Inline, LongField);

        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(long)));
        Assert.That(reader.Read(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.GetValue(0), Is.EqualTo(8L));
            Assert.That(reader.GetFieldValue<long>(0), Is.EqualTo(8L));
            Assert.That(reader.GetInt64(0), Is.EqualTo(8L));
            Assert.That(converted, Is.Zero);
        }
    }

    [Test]
    public async Task Configured_fallback_binds_the_source_and_converts_once_per_row()
    {
        var converted = 0;
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.AddProvider(new ValueReader(typeof(int), 3000000, secondRow: 2));
            options.AddValueConverter<int, long>(value => { converted++; return value * 1000L; }, "integer", "i64");
        });

        using var reader = await factory.OpenAsync(Inline, LongField);

        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(long)));
        using (var metadata = reader.GetSchemaTable())
            Assert.That(metadata!.Rows[0]["DataType"], Is.EqualTo(typeof(long)));
        Assert.That(reader.Read(), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.GetInt64(0), Is.EqualTo(3000000000L));
            Assert.That(reader.GetFieldValue<long>(0), Is.EqualTo(3000000000L));
            Assert.That(reader["value"], Is.EqualTo(3000000000L));
            var values = new object[1];
            Assert.That(reader.GetValues(values), Is.EqualTo(1));
            Assert.That(values[0], Is.TypeOf<long>());
            Assert.That(converted, Is.EqualTo(1));
        }

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo(2000L));
        Assert.That(converted, Is.EqualTo(2));
    }

    [Test]
    public void Binding_rejects_an_incompatible_declared_converter_output()
    {
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.AddProvider(new ValueReader(typeof(int), 1));
            options.AddValueConverter<int, string>(value => value.ToString(), targetType: typeof(long));
        });

        Assert.That(async () => await factory.OpenAsync(Inline, LongField),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("System.String").And.Message.Contains("System.Int64"));
    }

    [Test]
    public void Binding_rejects_a_native_binding_declaring_the_wrong_type()
    {
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.AddProvider(new ValueReader(typeof(long), 1L, incorrectBindingType: typeof(int)));
            options.AddRepresentationProfile("longs", _ => typeof(long));
        });

        Assert.That(async () => await factory.OpenAsync(Inline, LongField),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("System.Int64").And.Message.Contains("System.Int32"));
    }

    [Test]
    public void Binding_rejects_multiple_available_fallbacks()
    {
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.AddProvider(new ValueReader(typeof(int), 1));
            options.AddValueConverter<int, long>(value => value);
            options.AddValueConverter<int, long>(value => value * 1000L);
        });

        Assert.That(async () => await factory.OpenAsync(Inline, LongField),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("Multiple fallback converters"));
    }

    [TestCase("string", "i64", null)]
    [TestCase("integer", "i32", null)]
    [TestCase("integer", "i64", SpatialEncoding.Wkt)]
    public void Fallback_requires_matching_logical_type_format_and_encoding(string logicalType, string format, SpatialEncoding? encoding)
    {
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.AddProvider(new ValueReader(typeof(int), 1));
            options.AddRepresentationProfile("longs", _ => typeof(long));
            options.AddValueConverter<int, long>(value => value, logicalType, format, encoding);
        });

        Assert.That(async () => await factory.OpenAsync(Inline, LongField),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("value").And.Message.Contains("System.Int64"));
    }

    [Test]
    public async Task Delimited_fallback_preserves_converter_errors_and_skips_missing_before_source_parsing()
    {
        var calls = 0;
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.AddRepresentationProfile("labels", _ => typeof(int));
            options.AddValueConverter<JsonElement, int>(json =>
            {
                calls++;
                return json.GetProperty("label").GetInt32();
            }, "geojson", encoding: SpatialEncoding.GeoJson);
        }, new ValuesStream("value\r\nNA\r\n\"NA\"\r\n{\r\n\"{\"\"label\"\":7}\"\r\n"));
        var field = new DataField("value", "geojson", Extensions: Missing("NA"))
        {
            Spatial = new(Encoding: SpatialEncoding.GeoJson)
        };
        using var reader = await factory.OpenAsync(Csv, new DataSchema([field]));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(0), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo(DBNull.Value));
        Assert.That(calls, Is.Zero);
        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.GetValue(0), Throws.InstanceOf<JsonException>());
        Assert.That(calls, Is.Zero);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(7));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task Native_geopoint_binding_applies_field_markers_before_parsing()
    {
        var schema = new DataSchema([new("value", "geopoint", Extensions: Missing("NA"))], Extensions: Missing("other"));
        var factory = new ResourceReaderFactory(new ValuesStream("value\r\nNA\r\n\"NA\"\r\nother\r\n\"4,50\"\r\n"));
        using var reader = await factory.OpenAsync(Csv, schema);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo(DBNull.Value));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(0), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.GetValue(0), Throws.TypeOf<FormatException>());
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<GeoPoint>(0), Is.EqualTo(new GeoPoint(4, 50)));
    }

    [Test]
    public async Task Converted_json_survives_disposal_of_the_converter_source()
    {
        JsonElement result;
        using (var document = JsonDocument.Parse("""{"value":12}"""))
        {
            var factory = ResourceReaderFactory.Create(options =>
            {
                options.AddProvider(new ValueReader(typeof(string), "input"));
                options.AddRepresentationProfile("json", _ => typeof(JsonElement));
                options.AddValueConverter<string, JsonElement>(_ => document.RootElement);
            });
            using var reader = await factory.OpenAsync(Inline, LongField);

            Assert.That(reader.Read(), Is.True);
            result = reader.GetFieldValue<JsonElement>(0);
        }

        Assert.That(result.GetProperty("value").GetInt32(), Is.EqualTo(12));
    }

    private static ExtensionMetadata Missing(params string[] values) =>
        ExtensionMetadata.For("datapackage", new Dictionary<string, object?> { ["missingValues"] = values });

    private sealed class ValuesStream(string value) : IEndpointStreamResolver
    {
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(value), writable: false));
    }

    private sealed class ValueReader(Type nativeType, object value, Type? secondType = null,
        object? secondRow = null, Type? incorrectBindingType = null) : IDataEndpointReaderProvider, IReaderBindingProvider
    {
        public bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format) => true;

        public ReaderFieldBinding? TryBind(ReaderRepresentationRequest request)
            => request.RuntimeType == nativeType || request.RuntimeType == secondType
                ? new(incorrectBindingType ?? request.RuntimeType)
                : null;

        public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context, CancellationToken cancellationToken = default)
        {
            var table = new DataTable();
            table.Columns.Add("value", context.FieldBindings[0].RuntimeType);
            table.Rows.Add(value);
            if (secondRow is not null) table.Rows.Add(secondRow);
            return ValueTask.FromResult<IDataReader>(new OwnedDataReader(table.CreateDataReader(), table));
        }
    }
}
