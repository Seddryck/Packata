using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.DataPackage.Mapping;

namespace Packata.ResourceReaders.Testing;

public class CanonicalInlineReaderTests
{
    [Test]
    public async Task OpenAsync_reads_mapped_inline_dictionary_rows_with_schema_types()
    {
        var package = new Packata.DataPackage.DataPackage
        {
            Name = "sample",
            Resources =
            [
                new Packata.DataPackage.Resource
                {
                    Name = "data",
                    Data = new JArray(
                        new JObject { ["id"] = 1, ["name"] = "alpha" },
                        new JObject { ["id"] = 2, ["name"] = "beta" }),
                    Schema = new Packata.DataPackage.Schema
                    {
                        Fields =
                        [
                            new Packata.DataPackage.IntegerField { Name = "id", Type = "integer" },
                            new Packata.DataPackage.StringField { Name = "name", Type = "string" }
                        ]
                    }
                }
            ]
        };
        var contract = package.ToCanonicalContract().RequireValue();
        var asset = contract.Assets.Single();
        var endpoint = contract.Endpoints.Single();

        using var reader = await new ResourceReaderFactory().OpenAsync(endpoint, asset.Schema);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetName(0), Is.EqualTo("id"));
            Assert.That(reader["id"], Is.EqualTo(1));
            Assert.That(reader["name"], Is.EqualTo("alpha"));
        });
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["name"], Is.EqualTo("beta"));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public async Task OpenAsync_reads_positional_rows_when_a_schema_is_available()
    {
        var endpoint = Endpoint(new object?[] { new object?[] { "1", "alpha" } });
        var schema = new DataSchema(
            [new DataField("id", "integer"), new DataField("name", "string")]);

        using var reader = await new ResourceReaderFactory().OpenAsync(endpoint, schema);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader["id"], Is.EqualTo(1));
    }

    [Test]
    public void OpenAsync_rejects_scalar_inline_values()
    {
        Assert.That(async () => await new ResourceReaderFactory().OpenAsync(Endpoint("value")),
            Throws.TypeOf<ArgumentException>()
                .With.Message.Contains("enumerable collection of rows"));
    }

    [Test]
    public void OpenAsync_rejects_positional_rows_without_a_schema()
    {
        var endpoint = Endpoint(new object?[] { new object?[] { 1, "alpha" } });

        Assert.That(async () => await new ResourceReaderFactory().OpenAsync(endpoint),
            Throws.TypeOf<ArgumentException>()
                .With.Message.Contains("require a schema"));
    }

    private static DataEndpoint Endpoint(object? value) =>
        new("inline", "inline", EndpointKind.Inline, null, new InlineLocation(value));
}
