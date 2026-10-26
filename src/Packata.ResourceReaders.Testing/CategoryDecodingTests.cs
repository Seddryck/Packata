using System.Data;
using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.DataPackage.Mapping;
using Packata.OpenDataContract.Mapping;
using PocketCsvReader.Configuration;

namespace Packata.ResourceReaders.Testing;

public class CategoryDecodingTests
{
    [TestCase(false, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, false, false)]
    [TestCase(true, true, false)]
    [TestCase(false, true, true)]
    public async Task Both_formats_use_native_sequences_only_when_enabled(bool odcs, bool decode, bool registerDelimited)
    {
        var contract = Contract(odcs);
        var asset = contract.Assets.Single();
        var endpoint = contract.Endpoints.Single();
        var before = asset.Schema!.Fields[1].Constraints.Single().Value;
        var streams = new CsvStreams("direction,rating,other\r\nS,1,7\r\nN,2,8\r\nX,3,9\r\n\"S\",01,10\r\n,2,11\r\n");
        var factory = decode ? ResourceReaderFactory.Create(options =>
        {
            if (registerDelimited) options.AddDelimited();
            options.DecodeCategories = true;
        }, streams)
            : new ResourceReaderFactory(streams);
        using (var reader = await factory.OpenAsync(endpoint, asset.Schema))
        {
            Assert.That(reader.Read(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.GetValue(0), Is.EqualTo(decode ? "South" : "S"));
                Assert.That(reader["rating"], Is.EqualTo(decode ? (object)"Low" : 1));
                Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(string)));
                Assert.That(reader.GetFieldType(1), Is.EqualTo(decode ? typeof(string) : typeof(int)));
                Assert.That(reader.GetFieldType(2), Is.EqualTo(typeof(int)));
                Assert.That(reader.GetInt32(2), Is.EqualTo(7));
            });
            if (decode)
            {
                Assert.That(reader.GetDataTypeName(1), Is.EqualTo("string"));
                Assert.That(reader.GetString(1), Is.EqualTo("Low"));
                Assert.That(() => reader.GetInt32(1), Throws.TypeOf<FormatException>());
                var values = new object[3];
                Assert.That(reader.GetValues(values), Is.EqualTo(3));
                Assert.That(values, Is.EqualTo(new object[] { "South", "Low", 7 }));
                Assert.That(() => reader.GetSchemaTable(), Throws.TypeOf<NotImplementedException>());
            }
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetValue(0), Is.EqualTo("N")); // Entry without a label.
            Assert.That(reader.GetValue(1), Is.EqualTo(decode ? (object)"2" : 2));
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetValue(0), Is.EqualTo("X")); // Outside the catalog.
            Assert.That(reader.GetValue(1), Is.EqualTo(decode ? (object)"3" : 3));
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetValue(0), Is.EqualTo("S")); // Quoted codes bypass native sequences.
            Assert.That(reader.GetValue(1), Is.EqualTo(decode ? (object)"01" : 1)); // Exact text only.
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetValue(0), Is.EqualTo("")); // PocketCsvReader keeps empty strings.
            Assert.That(reader.GetValue(1), Is.EqualTo(decode ? (object)"2" : 2));
            Assert.That(reader.Read(), Is.False);
        }
        Assert.That(streams.Last!.CanRead, Is.False);
        Assert.That(asset.Schema.Fields[1].LogicalType, Is.EqualTo("integer"));
        Assert.That(asset.Schema.Fields[1].Constraints.Single().Value, Is.EqualTo(before));
        Assert.That(asset.Schema.Fields[0].ValueDomain!.Entries![0].Value.Value, Is.EqualTo("S"));
    }

    [Test]
    public async Task Number_boolean_empty_labels_and_columns_without_labels_keep_consistent_metadata()
    {
        var schema = new DataSchema([
            Field("number", "number", 1.5m, "One and a half"),
            Field("boolean", "boolean", true, "Yes"),
            Field("empty", "string", "A", ""),
            Field("raw", "integer", 1, null),
            new DataField("reference", "string", ValueDomain: new(reference: new("codes")))
        ]);
        var streams = new CsvStreams("number,boolean,empty,raw,reference\r\n1.5,true,A,1,S\r\n2.5,false,X,2,N\r\n");
        using var reader = await ResourceReaderFactory.Create(options => options.DecodeCategories = true, streams)
            .OpenAsync(Endpoint(), schema);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo("One and a half"));
        Assert.That(reader.GetValue(1), Is.EqualTo("Yes"));
        Assert.That(reader.GetString(2), Is.Empty);
        Assert.That(reader.IsDBNull(2), Is.False);
        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(string)));
        Assert.That(reader.GetFieldType(1), Is.EqualTo(typeof(string)));
        Assert.That(reader.GetFieldType(3), Is.EqualTo(typeof(int)));
        Assert.That(reader.GetValue(4), Is.EqualTo("S")); // References are not fetched/expanded.
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("2.5"));
        Assert.That(reader.GetString(1), Is.EqualTo("false"));
    }

    [TestCase("xlsx")]
    [TestCase("parquet")]
    [TestCase("unknown")]
    [TestCase("inline")]
    public void Unsupported_engines_fail_before_opening_streams(string format)
    {
        var streams = new CsvStreams("unused");
        var factory = ResourceReaderFactory.Create(options => options.DecodeCategories = true, streams);
        var endpoint = format == "inline" ? Endpoint() with { Location = new InlineLocation(Array.Empty<object>()) } : Endpoint(format);
        Assert.That(async () => await factory.OpenAsync(endpoint), Throws.TypeOf<CategoryDecodingNotSupportedException>()
            .With.Message.Contains("PocketCsvReader"));
        Assert.That(streams.Last, Is.Null);
    }

    [Test]
    public void Native_category_sequences_preserve_null_sequences()
    {
        var schema = new SchemaDescriptorBuilder().Indexed();
        PocketCsvReaderProviderDefaults.AddField(schema, new Core.RuntimeTypeMapper(), Field("code", "integer", 1, "Low"), true);
        // Null sequences belong to the native engine and remain independent of category labels.
        var csv = new CsvReaderBuilder().WithDialect(dialect => dialect.WithHeader())
            .WithSchema(schema).WithResource(resource => resource.WithSequence("NULL", null)).Build();
        using var reader = csv.ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes("code\r\nNULL\r\n1\r\n")));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(0), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo("Low"));
    }

    [Test]
    public void Pocket_sequences_skip_quoted_codes_and_replace_before_integer_parsing()
    {
        var strings = new CsvReaderBuilder().WithDialect(dialect => dialect.WithHeader())
            .WithSchema(schema => schema.Indexed().WithField(typeof(string), "code", field => field.WithSequence("S", "South"))).Build();
        using var reader = strings.ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes("code\r\nS\r\n\"S\"\r\nX\r\n\r\n")));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo("South"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo("S"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo("X"));
        var integers = new CsvReaderBuilder().WithDialect(dialect => dialect.WithHeader())
            .WithSchema(schema => schema.Indexed().WithField(typeof(int), "code", field => field.WithSequence("1", "Low"))).Build();
        using var numeric = integers.ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes("code\r\n1\r\n")));
        Assert.That(() => { numeric.Read(); _ = numeric.GetValue(0); }, Throws.Exception);
    }

    private static DataField Field(string name, string type, object value, string? label)
    {
        var scalar = ScalarValue.From(value);
        return new(name, type, ValueDomain: new([new(scalar, label)], [scalar]));
    }
    private static DataEndpoint Endpoint(string format = "csv") => new("data", "data", EndpointKind.File, null,
        new PathLocation(["data.csv"]), new DataFormat(format));
    private sealed class CsvStreams(string csv) : IEndpointStreamResolver
    {
        public MemoryStream? Last { get; private set; }
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream>(Last = new MemoryStream(Encoding.UTF8.GetBytes(csv)));
    }
    private static DataContract Contract(bool odcs)
    {
        var text = odcs ? """
            apiVersion: v3.2.0
            kind: DataContract
            id: directions
            version: 1.0.0
            status: draft
            schema:
              - name: data
                physicalType: file
                properties:
                  - name: direction
                    logicalType: string
                    enum:
                      - value: S
                        label: South
                      - value: N
                  - name: rating
                    logicalType: integer
                    enum:
                      - value: 1
                        label: Low
                      - value: 2
                  - name: other
                    logicalType: integer
            servers:
              - server: local
                type: local
                path: data.csv
                format: csv
            """ : """
            {"resources":[{"name":"data","path":"data.csv","format":"csv","dialect":{"header":true},"schema":{"fields":[
              {"name":"direction","type":"string","categories":[{"value":"S","label":"South"},{"value":"N"}]},
              {"name":"rating","type":"integer","categories":[{"value":1,"label":"Low"},{"value":2}]},
              {"name":"other","type":"integer"}
            ]}}]}
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return odcs ? new OpenDataContract.DataContractFactory().LoadFromStream(stream).ToCanonicalContract().RequireValue()
            : new DataPackage.DataPackageFactory().LoadFromStream(stream).ToCanonicalContract().RequireValue();
    }
}
