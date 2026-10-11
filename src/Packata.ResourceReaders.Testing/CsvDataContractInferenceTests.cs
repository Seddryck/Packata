using System.Globalization;
using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using PocketCsvReader;

namespace Packata.ResourceReaders.Testing;

public class CsvDataContractInferenceTests
{
    [Test]
    public void Infer_maps_header_types_dialect_and_source_binding()
    {
        using var stream = Csv("Name,Age,Active\r\nAlice,42,true\r\nBob,35,false\r\n");

        var result = new CsvDataContractInference().Infer(stream, "imports/people.csv");

        var asset = result.Contract.Assets.Single();
        var endpoint = result.Contract.Endpoints.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Contract.Identity.Id, Is.EqualTo("people-contract"));
            Assert.That(asset.Id, Is.EqualTo("people"));
            Assert.That(asset.Schema!.Fields.Select(x => x.Name), Is.EqualTo(new[] { "Name", "Age", "Active" }));
            Assert.That(asset.Schema.Fields.Select(x => x.LogicalType),
                Is.EqualTo(new[] { "string", "integer", "boolean" }));
            Assert.That(asset.Schema.Fields[1].Format, Is.EqualTo("i16"));
            Assert.That(asset.Schema.Fields.All(x => x.Required), Is.True);
            Assert.That(asset.EndpointBindings.Single().EndpointId, Is.EqualTo(endpoint.Id));
            Assert.That(((PathLocation)endpoint.Location).Paths, Is.EqualTo(new[] { "imports/people.csv" }));
            Assert.That(endpoint.Format!.Name, Is.EqualTo("csv"));
            Assert.That(endpoint.Format.Options["delimiter"], Is.EqualTo(','));
            Assert.That(endpoint.Format.Options["header"], Is.True);
            Assert.That(endpoint.Format.Options["lineTerminator"], Is.EqualTo("\r\n"));
        });
    }

    [Test]
    public void Infer_generates_column_names_when_header_is_absent()
    {
        using var stream = Csv("1,alpha\n2,beta\n");
        var options = Options(new CsvInferenceOptions
        {
            Header = CsvHeaderMode.Absent,
            Dialect = Dialect()
        });

        var result = new CsvDataContractInference(options).Infer(stream, "values.csv");

        Assert.That(result.Contract.Assets.Single().Schema!.Fields.Select(x => x.Name),
            Is.EqualTo(new[] { "Column1", "Column2" }));
    }

    [Test]
    public void Infer_maps_configured_nulls_to_optional_fields()
    {
        using var stream = Csv("Id,Note\n1,alpha\n2,NULL\n");
        var options = Options(new CsvInferenceOptions
        {
            Header = CsvHeaderMode.Present,
            Dialect = Dialect() with { NullSequence = "NULL" }
        });

        var fields = new CsvDataContractInference(options).Infer(stream, "nullable.csv")
            .Contract.Assets.Single().Schema!.Fields;

        Assert.Multiple(() =>
        {
            Assert.That(fields.Single(x => x.Name == "Id").Required, Is.True);
            Assert.That(fields.Single(x => x.Name == "Note").Required, Is.False);
        });
    }

    [Test]
    public void Infer_keeps_mixed_values_and_leading_zero_identifiers_as_strings()
    {
        using var stream = Csv("Code,Value\n00123,10\n00456,oops\n");
        var options = Options(new CsvInferenceOptions
        {
            Header = CsvHeaderMode.Present,
            Dialect = Dialect()
        });

        var fields = new CsvDataContractInference(options).Infer(stream, "identifiers.csv")
            .Contract.Assets.Single().Schema!.Fields;

        Assert.That(fields.Select(x => x.LogicalType), Is.EqualTo(new[] { "string", "string" }));
    }

    [Test]
    public void Infer_delegates_custom_dialect_and_culture_options()
    {
        using var stream = Csv("Amount;Paid\n1,5;true\n2,75;false\n");
        var options = Options(new CsvInferenceOptions
        {
            InferDialect = true,
            Header = CsvHeaderMode.Present,
            Culture = CultureInfo.GetCultureInfo("fr-FR")
        });

        var result = new CsvDataContractInference(options).Infer(stream, "payments.txt");
        var endpoint = result.Contract.Endpoints.Single();
        var amount = result.Contract.Assets.Single().Schema!.Fields[0];

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.Format!.Options["delimiter"], Is.EqualTo(';'));
            Assert.That(amount.LogicalType, Is.EqualTo("number"));
        });
    }

    [Test]
    public void Infer_preserves_ambiguous_decisions_and_confidence()
    {
        using var stream = Csv("Name,Place\nAlice,Brussels\nBob,Paris\n");
        var options = Options(new CsvInferenceOptions { Dialect = Dialect() });

        var result = new CsvDataContractInference(options).Infer(stream, "places.csv");
        var diagnostic = result.Diagnostics.Single(x => x.Code == "header.ambiguous");

        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Severity, Is.EqualTo(InferenceSeverity.Warning));
            Assert.That(diagnostic.Confidence, Is.Not.Null);
            Assert.That(diagnostic.SourcePath, Is.EqualTo("places.csv"));
            Assert.That(result.HasWarnings, Is.True);
        });
    }

    [Test]
    public void Infer_prefers_caller_provided_contract_and_asset_metadata()
    {
        using var stream = Csv("Id\n1\n");
        var options = Options(new CsvInferenceOptions
        {
            Header = CsvHeaderMode.Present,
            Dialect = Dialect()
        }) with
        {
            ContractIdentity = new ContractIdentity("sales-v2", "Sales", "2.0", "draft"),
            ContractMetadata = new ContractMetadata("Sales import", "Imported sales records"),
            AssetId = "sales-records",
            AssetName = "Sales records",
            AssetPhysicalName = "source-sales.csv",
            AssetDescription = "Daily sales",
            EndpointId = "sales-upload",
            EndpointName = "Sales upload"
        };

        var result = new CsvDataContractInference(options).Infer(stream, "ignored.csv");
        var asset = result.Contract.Assets.Single();
        var endpoint = result.Contract.Endpoints.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Contract.Identity, Is.EqualTo(options.ContractIdentity));
            Assert.That(result.Contract.Metadata, Is.EqualTo(options.ContractMetadata));
            Assert.That(asset.Id, Is.EqualTo("sales-records"));
            Assert.That(asset.Name, Is.EqualTo("Sales records"));
            Assert.That(asset.PhysicalName, Is.EqualTo("source-sales.csv"));
            Assert.That(asset.Description, Is.EqualTo("Daily sales"));
            Assert.That(endpoint.Id, Is.EqualTo("sales-upload"));
            Assert.That(endpoint.Name, Is.EqualTo("Sales upload"));
        });
    }

    private static CsvDataContractInferenceOptions Options(CsvInferenceOptions inference) =>
        new() { InferenceOptions = inference };

    private static DialectDescriptor Dialect() =>
        new(Header: false, HeaderRows: [], LineTerminator: "\n");

    private static MemoryStream Csv(string value) => new(Encoding.UTF8.GetBytes(value));
}
