using System.Net;
using System.Text;
using NUnit.Framework;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Testing;

public class InternetValueReaderTests
{
    private static readonly DataEndpoint Csv = new("data", "data", EndpointKind.File, null, new PathLocation(["data.csv"]));

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task Profiles_independently_type_only_their_declared_string_formats(bool useUris, bool useNetwork)
    {
        const string absolute = "https://example.com/a";
        const string relative = "../a";
        const string ipv4 = "192.0.2.1";
        const string ipv6 = "2001:db8::1";
        const string hostname = "example.com";
        var factory = ResourceReaderFactory.Create(options =>
        {
            if (useUris) options.UseUriValues();
            if (useNetwork) options.UseNetworkValues();
        }, new CsvStream($"absolute,reference,v4,v6,host,text\r\n{absolute},{relative},{ipv4},{ipv6},{hostname},{ipv4}\r\n"));
        var schema = new DataSchema(
        [
            new("absolute", "string", Format: "uri"),
            new("reference", "string", Format: "uri-reference"),
            new("v4", "string", Format: "ipv4"),
            new("v6", "string", Format: "ipv6"),
            new("host", "string", Format: "hostname"),
            new("text", "string")
        ]);

        using var reader = await factory.OpenAsync(Csv, schema);

        Assert.That(reader.Read(), Is.True);
        object[] expected =
        [
            useUris ? new Uri(absolute, UriKind.Absolute) : absolute,
            useUris ? new Uri(relative, UriKind.Relative) : relative,
            useNetwork ? IPAddress.Parse(ipv4) : ipv4,
            useNetwork ? IPAddress.Parse(ipv6) : ipv6,
            hostname,
            ipv4
        ];
        using (Assert.EnterMultipleScope())
        {
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.That(reader.GetFieldType(index), Is.EqualTo(expected[index].GetType()));
                Assert.That(reader.GetValue(index), Is.EqualTo(expected[index]));
            }
        }
    }

    [Test]
    public async Task Uri_reference_can_also_be_absolute()
    {
        const string absolute = "urn:example:record";
        var factory = ResourceReaderFactory.Create(options => options.UseUriValues(),
            new CsvStream($"value\r\n{absolute}\r\n"));
        var schema = new DataSchema([new("value", "string", Format: "uri-reference")]);

        using var reader = await factory.OpenAsync(Csv, schema);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo(new Uri(absolute, UriKind.Absolute)));
        Assert.That(FieldTypeResolver.Resolve(schema.Fields[0], TypeMappingOptions.Strict).RuntimeType,
            Is.EqualTo(typeof(string)));
    }

    [TestCase("uri", "../a")]
    [TestCase("uri", "https://example.com/a b")]
    [TestCase("uri-reference", "../a b")]
    [TestCase("ipv4", "2001:db8::1")]
    [TestCase("ipv4", "192.1")]
    [TestCase("ipv4", "192.0.2.010")]
    [TestCase("ipv4", "192.0.2.256")]
    [TestCase("ipv6", "192.0.2.1")]
    [TestCase("ipv6", "[2001:db8::1]")]
    [TestCase("ipv6", "fe80::1%3")]
    public async Task Reject_values_outside_the_declared_format(string format, string value)
    {
        var factory = ResourceReaderFactory.Create(options => options.UseUriValues().UseNetworkValues(),
            new CsvStream($"value\r\n{value}\r\n"));
        using var reader = await factory.OpenAsync(Csv, new DataSchema([new("value", "string", Format: format)]));

        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.GetValue(0), Throws.InstanceOf<FormatException>());
    }

    private sealed class CsvStream(string content) : IEndpointStreamResolver
    {
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false));
    }
}
