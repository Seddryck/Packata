using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.DataPackage.Mapping;

namespace Packata.ResourceReaders.Testing;

public class CanonicalInlineReaderTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Null_values_use_DBNull_and_underlying_types_independently_of_schema_flags(bool required, bool nullable)
    {
        var schema = new DataSchema([new("id", "integer", Required: required, Nullable: nullable)]);
        var rows = new object?[] { new object?[] { null } };
        using var table = Tabular.InlineDataReader.CreateTable(rows, schema);
        Assert.That(table.Columns[0].DataType, Is.EqualTo(typeof(int)));
        Assert.That(table.Rows[0][0], Is.EqualTo(DBNull.Value));
        using var reader = await new ResourceReaderFactory().OpenAsync(Endpoint(rows), schema);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(int)));
        Assert.That(reader.IsDBNull(0), Is.True);
    }

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
