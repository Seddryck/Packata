using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.DataPackage.Mapping;

namespace Packata.ResourceReaders.Testing;

public class CategoryTableDecodingTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Named_lookup_is_reused_in_either_resource_order_only_when_enabled(bool first, bool decode)
    {
        var contract = Contract(first);
        var streams = new Streams();
        var factory = Factory(streams, decode);
        using (var reader = await factory.OpenAsync(Request(contract)))
        {
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetValue(0), Is.EqualTo(decode ? "Regular" : "REG"));
            Assert.That(reader.GetValue(1), Is.EqualTo(decode ? "Student" : "STU"));
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetValue(0), Is.EqualTo("X"));
            Assert.That(reader.GetValue(1), Is.EqualTo("REG")); // quoted codes bypass native sequences
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetValue(0), Is.EqualTo(""));
            Assert.That(reader.GetValue(1), Is.EqualTo(decode ? "" : "HON"));
        }
        Assert.That(streams.Opened.Count(x => x.Path == "lookup.csv"), Is.EqualTo(decode ? 1 : 0));
        Assert.That(streams.Opened.All(x => !x.Stream.CanRead), Is.True);
        Assert.That(Request(contract).Schema!.Fields[0].ValueDomain!.Entries, Is.Null);
        if (decode)
        {
            streams.Lookup = "key,title\r\nREG,Changed\r\nSTU,Student\r\nHON,\r\n";
            using var second = await factory.OpenAsync(Request(contract));
            Assert.That(second.Read(), Is.True);
            Assert.That(second.GetString(0), Is.EqualTo("Changed")); // no factory/global cache
        }
    }

    [TestCase("key,title\r\n,Missing\r\n", "missing required")]
    [TestCase("key,title\r\nREG,First\r\nREG,Second\r\n", "duplicate mapped value")]
    [TestCase("key,title\r\nREG,Same\r\nSTU,Same\r\n", "duplicate non-null mapped label")]
    [TestCase("key,title\r\nREG,\r\nSTU,\r\n", "duplicate non-null mapped label")]
    public void Invalid_rows_fail_before_consumer_opens_and_release_streams(string csv, string message)
    {
        var streams = new Streams { Lookup = csv };
        Assert.That(async () => await Factory(streams).OpenAsync(Request(Contract())),
            Throws.TypeOf<CategoryTableDecodingException>().With.Message.Contains(message));
        Assert.That(streams.Opened.Select(x => x.Path), Is.EqualTo(new[] { "lookup.csv" }));
        Assert.That(streams.Opened.All(x => !x.Stream.CanRead), Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Limits_release_lookup_before_opening_consumer(bool rows)
    {
        var streams = new Streams();
        var factory = ResourceReaderFactory.Create(options =>
        {
            options.DecodeCategories = true;
            options.CategoryTableRowLimit = rows ? 1 : 100;
            options.CategoryTableByteLimit = rows ? 1000 : 8;
        }, streams);
        Assert.That(async () => await factory.OpenAsync(Request(Contract())),
            Throws.TypeOf<CategoryTableDecodingException>().With.Message.Contains(rows ? "row limit" : "byte limit"));
        Assert.That(streams.Opened.All(x => !x.Stream.CanRead), Is.True);
        Assert.That(streams.Opened.Any(x => x.Path == "data.csv"), Is.False);
    }

    [Test]
    public void Cancellation_during_lookup_loading_releases_stream()
    {
        using var cancellation = new CancellationTokenSource();
        var streams = new Streams { OnOpen = () => cancellation.Cancel() };
        Assert.That(async () => await Factory(streams).OpenAsync(Request(Contract()), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(streams.Opened.All(x => !x.Stream.CanRead), Is.True);
        Assert.That(streams.Opened.Count, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Label_encoding_skips_lookup_even_when_unavailable(bool integer)
    {
        var request = Request(Contract(integer: integer));
        request = request with { Schema = request.Schema! with { Fields = request.Schema.Fields.Select(field =>
            field with { ValueDomain = new(reference: new("missing", encodedAs: ValueEncoding.Label)) }).ToArray() } };
        var streams = new Streams();
        using var reader = await Factory(streams).OpenAsync(request);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("REG"));
        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(string)));
        Assert.That(streams.Opened.Select(x => x.Path), Is.EqualTo(new[] { "data.csv" }));
    }

    [Test]
    public void Pre_cancelled_request_opens_no_streams()
    {
        var streams = new Streams();
        Assert.That(async () => await Factory(streams).OpenAsync(Request(Contract()), new CancellationToken(true)),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(streams.Opened, Is.Empty);
    }

    [Test]
    public async Task Foreign_key_alone_never_loads_lookup()
    {
        var request = Request(Contract());
        request = request with { Schema = request.Schema! with
        {
            Fields = request.Schema.Fields.Select(field => field with { ValueDomain = null }).ToArray(),
            Relationships = [new DataRelationship(["code"], "lookup", ["key"])]
        } };
        var streams = new Streams();
        using var reader = await Factory(streams).OpenAsync(request);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("REG"));
        Assert.That(streams.Opened.Select(x => x.Path), Is.EqualTo(new[] { "data.csv" }));
    }

    [TestCase("external", "external-package")]
    [TestCase("missing", "could not be resolved")]
    [TestCase("cycle", "cyclic")]
    [TestCase("engine", "PocketCsvReader")]
    [TestCase("remote", "local")]
    [TestCase("context", "containing contract")]
    public void Unsupported_references_have_explicit_diagnostics_without_opening_streams(string kind, string diagnostic)
    {
        var contract = Contract();
        var request = Request(contract);
        if (kind is "external" or "missing")
        {
            request = request with { Schema = new DataSchema([new DataField("code", "string", ValueDomain: new(reference:
                new(kind == "missing" ? "missing" : "lookup", kind == "external" ? new(PackageLocatorKind.RegistryName, "other") : null)))]) };
        }

        if (kind == "context") request = request with { Contract = null };
        if (kind == "cycle")
        {
            var lookup = contract.Assets.Single(x => x.Name == "lookup");
            lookup = lookup with { Schema = new DataSchema([new DataField("key", "string", ValueDomain: new(reference: new("lookup")))]) };
            request = request with { Contract = contract with { Assets = contract.Assets.Where(x => x.Name != "lookup").Append(lookup).ToArray() } };
        }
        if (kind is "engine" or "remote")
        {
            var id = contract.Assets.Single(x => x.Name == "lookup").EndpointBindings[0].EndpointId;
            request = request with { Contract = contract with { Endpoints = contract.Endpoints.Select(endpoint => endpoint.Id != id ? endpoint :
                kind == "engine" ? endpoint with { Format = new DataFormat("xlsx") } : endpoint with { Location = new PathLocation(["https://example.com/lookup.csv"]) }).ToArray() } };
        }
        var streams = new Streams();
        Assert.That(async () => await Factory(streams).OpenAsync(request), Throws.TypeOf<CategoryTableDecodingException>().With.Message.Contains(diagnostic));
        Assert.That(streams.Opened, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Integer_values_are_checked_and_labeled_columns_report_strings(bool invalid)
    {
        var contract = Contract(integer: true);
        var streams = new Streams { Lookup = invalid ? "key,title\r\nwrong,Regular\r\n" : "key,title\r\n1,Regular\r\n2,Student\r\n", Data = "code,other\r\n1,2\r\n3,01\r\n" };
        if (invalid)
        {
            Assert.That(async () => await Factory(streams).OpenAsync(Request(contract)),
                Throws.TypeOf<CategoryTableDecodingException>().With.Message.Contains("incompatible with integer"));
            Assert.That(streams.Opened.All(x => !x.Stream.CanRead), Is.True);
            return;
        }
        using var reader = await Factory(streams).OpenAsync(Request(contract));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("Regular"));
        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(string)));
        Assert.That(reader.GetDataTypeName(0), Is.EqualTo("string"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("3"));
        Assert.That(reader.GetString(1), Is.EqualTo("01"));
    }

    [Test]
    public async Task Absent_label_mapping_keeps_integer_type_and_codes()
    {
        var contract = Contract(integer: true);
        contract = contract with { Assets = contract.Assets.Select(asset => asset.Name == "lookup"
            ? asset with { CategoryTable = new(new("key")) } : asset).ToArray() };
        var streams = new Streams { Lookup = "key,title\r\n1,Regular\r\n", Data = "code,other\r\n1,2\r\n" };
        using var reader = await Factory(streams).OpenAsync(Request(contract));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(int)));
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
    }

    private static ResourceReaderFactory Factory(Streams streams, bool decode = true) =>
        ResourceReaderFactory.Create(options => options.DecodeCategories = decode, streams);

    private static DataEndpointReadRequest Request(DataContract contract)
    {
        var asset = contract.Assets.Single(x => x.Name == "data");
        return new(contract.Endpoints.Single(x => x.Id == asset.EndpointBindings[0].EndpointId), asset.Schema, Contract: contract);
    }

    private static DataContract Contract(bool first = false, bool integer = false)
    {
        var type = integer ? "integer" : "string";
        var data = $$$"""
            {"name":"data","path":"data.csv","format":"csv","dialect":{"header":true},"schema":{"fields":[
            {"name":"code","type":"{{{type}}}","categories":{"resource":"lookup"}},
            {"name":"other","type":"{{{type}}}","categories":{"resource":"lookup","encodedAs":"value"}}]}}
            """;
        var lookup = $$$"""
            {"name":"lookup","type":"table","path":"lookup.csv","format":"csv","dialect":{"header":true},
            "categoryFieldMap":{"value":"key","label":"title"},"schema":{"fields":[
            {"name":"key","type":"{{{type}}}","constraints":{"required":true,"unique":true}},
            {"name":"title","type":"string","constraints":{"unique":true}}]}}
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"resources\":[" + (first ? lookup + "," + data : data + "," + lookup) + "]}"));
        return new DataPackage.DataPackageFactory().LoadFromStream(stream).ToCanonicalContract().RequireValue();
    }

    private sealed class Streams : IEndpointStreamResolver
    {
        public string Lookup { get; set; } = "key,title\r\nREG,Regular\r\nSTU,Student\r\nHON,\r\n";
        public string Data { get; set; } = "code,other\r\nREG,STU\r\nX,\"REG\"\r\n,HON\r\n";
        public Action? OnOpen { get; init; }
        public List<(string Path, MemoryStream Stream)> Opened { get; } = [];
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(path == "lookup.csv" ? Lookup : Data));
            Opened.Add((path, stream));
            OnOpen?.Invoke();
            return ValueTask.FromResult<Stream>(stream);
        }
    }
}
