using System.Collections;
using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;
using Packata.OpenDataContract.Mapping;

namespace Packata.ResourceReaders.Testing;

public class OdcsCategoryTableTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ODCS_uses_shared_loader_only_for_value_encoded_decoding(bool decode, bool labelEncoded)
    {
        var document = Document();
        if (labelEncoded)
        {
            foreach (var property in document.Schema[0].Properties)
                ((IDictionary)property.CustomProperties[0].Value!)["encodedAs"] = "label";
        }

        var contract = document.ToCanonicalContract().RequireValue();
        var streams = new Streams();
        var factory = ResourceReaderFactory.Create(options => options.DecodeCategories = decode, streams);
        using (var reader = await factory.OpenAsync(Request(contract)))
        {
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo(decode && !labelEncoded ? "Regular" : "REG"));
            Assert.That(reader.GetString(1), Is.EqualTo(decode && !labelEncoded ? "Student" : "STU"));
        }
        Assert.That(streams.LookupOpens, Is.EqualTo(decode && !labelEncoded ? 1 : 0));
        Assert.That(streams.Opened.All(stream => !stream.CanRead), Is.True);
        Assert.That(contract.Assets[0].Schema!.Fields[0].ValueDomain!.Entries, Is.Null);
    }

    [Test]
    public async Task ODCS_reference_without_label_mapping_keeps_codes()
    {
        var document = Document();
        ((IDictionary)document.Schema[1].CustomProperties[0].Value!).Remove("label");
        var contract = document.ToCanonicalContract().RequireValue();
        var streams = new Streams();
        using var reader = await ResourceReaderFactory.Create(options => options.DecodeCategories = true, streams)
            .OpenAsync(Request(contract));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("REG"));
        Assert.That(streams.LookupOpens, Is.EqualTo(1));
    }

    [Test]
    public async Task ODCS_foreign_keys_alone_do_not_trigger_lookup_loading()
    {
        var document = Document();
        foreach (var property in document.Schema[0].Properties)
        {
            property.CustomProperties.Clear();
            property.Relationships = [new() { To = "schema/categories/properties/key" }];
        }
        var contract = document.ToCanonicalContract().RequireValue();
        Assert.That(contract.Assets[0].Schema!.Relationships, Has.Count.EqualTo(2));
        var streams = new Streams();
        using var reader = await ResourceReaderFactory.Create(options => options.DecodeCategories = true, streams)
            .OpenAsync(Request(contract));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("REG"));
        Assert.That(streams.LookupOpens, Is.Zero);
    }

    private static DataEndpointReadRequest Request(DataContract contract)
    {
        var asset = contract.Assets[0];
        return new(contract.Endpoints.Single(endpoint => endpoint.Id == asset.EndpointBindings.Single().EndpointId), asset.Schema, Contract: contract);
    }

    private static OpenDataContract.DataContract Document()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            {"id":"members","servers":[{"server":"files","type":"local","path":"./","format":"csv"}],
            "schema":[{"name":"members","physicalName":"members.csv","physicalType":"table","properties":[
            {"name":"code","logicalType":"string","customProperties":[{"vendor":"packata","property":"categories","value":{"resource":"categories"}}]},
            {"name":"other","logicalType":"string","customProperties":[{"vendor":"packata","property":"categories","value":{"resource":"categories"}}]}]},
            {"name":"categories","physicalName":"categories.csv","physicalType":"table",
            "customProperties":[{"vendor":"packata","property":"categoryFieldMap","value":{"value":"key","label":"title"}}],
            "properties":[{"name":"key","logicalType":"string","required":true,"unique":true},
            {"name":"title","logicalType":"string","unique":true}]}]}
            """));
        return new OpenDataContract.DataContractFactory().LoadFromStream(stream);
    }

    private sealed class Streams : IEndpointStreamResolver
    {
        public int LookupOpens { get; private set; }
        public List<MemoryStream> Opened { get; } = [];
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            var lookup = Path.GetFileName(path) == "categories.csv";
            if (lookup) LookupOpens++;
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(lookup ? "key,title\r\nREG,Regular\r\nSTU,Student\r\n"
                : "code,other\r\nREG,STU\r\n"));
            Opened.Add(stream);
            return ValueTask.FromResult<Stream>(stream);
        }
    }
}
