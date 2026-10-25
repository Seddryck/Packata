using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.DataPackage.Export;
using Packata.DataPackage.Mapping;

namespace Packata.DataPackage.Testing.Export;

[TestFixture]
public sealed class CsvDialectExporterTests
{
    private static DataEndpoint Endpoint(string id = "csv", DataFormat? format = null) =>
        new(id, null, EndpointKind.File, null, new PathLocation(["missing.csv"]), format);

    private static DataContract Contract(params DataEndpoint[] endpoints) =>
        new(new("contract"), new(), [], endpoints, new());

    [Test]
    public void Export_SelectsEndpointAndPreservesCustomSettingsWithoutResourceMetadata()
    {
        var options = new Dictionary<string, object?>
        {
            ["delimiter"] = ';', ["headerRows"] = new long[] { 2, 3 }, ["headerJoin"] = ".",
            ["commentRows"] = new[] { 1 }, ["commentChar"] = '#', ["quoteChar"] = "'",
            ["doubleQuote"] = false, ["lineTerminator"] = "\n", ["nullSequence"] = "NA",
            ["skipInitialSpace"] = true
        };
        var selected = Endpoint("europe", new("csv", "text/csv", "utf-8", "gzip", options));
        var contract = Contract(Endpoint("default"), selected);
        var result = new CsvDialectExporter().Export(contract, "europe");
        var json = JObject.Parse(result.RequireValue());
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That((string?)json["$schema"], Is.EqualTo(CsvDialectExporter.Profile));
            Assert.That((string?)json["delimiter"], Is.EqualTo(";"));
            Assert.That(json["headerRows"]!.Values<int>(), Is.EqualTo(new[] { 2, 3 }));
            Assert.That((string?)json["headerJoin"], Is.EqualTo("."));
            Assert.That(json["commentRows"]!.Values<int>(), Is.EqualTo(new[] { 1 }));
            Assert.That((string?)json["commentChar"], Is.EqualTo("#"));
            Assert.That((string?)json["quoteChar"], Is.EqualTo("'"));
            Assert.That((bool?)json["doubleQuote"], Is.False);
            Assert.That((string?)json["lineTerminator"], Is.EqualTo("\n"));
            Assert.That((string?)json["nullSequence"], Is.EqualTo("NA"));
            Assert.That((bool?)json["skipInitialSpace"], Is.True);
            Assert.That(json.Properties().Select(p => p.Name), Does.Not.Contain("encoding").And.Not.Contain("compression").And.Not.Contain("path").And.Not.Contain("type").And.Not.Contain("headerRepeat").And.Not.Contain("resources"));
            Assert.That(selected.Format!.Options["delimiter"], Is.EqualTo(';'));
        });
        Assert.That((string?)JObject.Parse(new CsvDialectExporter().Export(contract, "default").RequireValue())["delimiter"], Is.EqualTo(","));
    }

    [Test]
    public void Export_UsesModelDefaultsAndSupportsHeaderlessEscapedCsv()
    {
        var exporter = new CsvDialectExporter();
        var defaults = JObject.Parse(exporter.Export(Contract(Endpoint()), "csv").RequireValue());
        Assert.That((bool?)defaults["header"], Is.True);
        Assert.That((string?)defaults["lineTerminator"], Is.EqualTo(new TableDelimitedDialect().LineTerminator));
        var result = exporter.Export(Contract(Endpoint(format: new("csv", Options: new Dictionary<string, object?>
        { ["header"] = false, ["quoteChar"] = null, ["escapeChar"] = "\\", ["doubleQuote"] = false }))), "csv");
        var json = JObject.Parse(result.RequireValue());
        Assert.That((bool?)json["header"], Is.False);
        Assert.That(json["headerRows"]!.Count(), Is.Zero);
        Assert.That((string?)json["quoteChar"], Is.Empty);
        Assert.That((string?)json["escapeChar"], Is.EqualTo("\\"));
        Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("CSV007"));
        var options = json.Properties().Where(p => p.Name != "$schema").ToDictionary(p => p.Name, p => (object?)p.Value);
        Assert.That(exporter.Export(Contract(Endpoint(format: new("csv", Options: options))), "csv").IsSuccessful, Is.True);
    }

    [TestCase(null, "CSV001")]
    [TestCase("", "CSV001")]
    [TestCase("absent", "CSV002")]
    public void Export_RejectsMissingSelection(string? id, string code)
    {
        var result = new CsvDialectExporter().Export(Contract(Endpoint()), id!);
        Assert.That(result.IsSuccessful, Is.False);
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(code));
    }

    [Test]
    public void Export_RejectsAmbiguousAndNonCsvEndpoints()
    {
        var exporter = new CsvDialectExporter();
        Assert.That(exporter.Export(Contract(Endpoint(), Endpoint()), "csv").Diagnostics.Single().Message, Does.Contain("ambiguous"));
        foreach (var endpoint in new[]
        {
            Endpoint(format: new("parquet")), Endpoint(format: new(null, "application/json")),
            Endpoint() with { Kind = EndpointKind.Database },
            Endpoint() with { Location = new InlineLocation("csv") },
            Endpoint() with { Location = new PathLocation(["one.csv", "two.json"]) }
        })
            Assert.That(exporter.Export(Contract(endpoint), "csv").Diagnostics.Single().Code, Is.EqualTo("CSV003"));
    }

    [TestCase("delimiter", 42)]
    [TestCase("header", "false")]
    [TestCase("delimiter", "")]
    [TestCase("lineTerminator", "")]
    [TestCase("quoteChar", "ab")]
    [TestCase("headerRows", "1")]
    [TestCase("headerJoin", null)]
    public void Export_RejectsInvalidSettings(string name, object? value)
    {
        var endpoint = Endpoint(format: new("csv", Options: new Dictionary<string, object?> { [name] = value }));
        var result = new CsvDialectExporter().Export(Contract(endpoint), "csv");
        Assert.That(result.IsSuccessful, Is.False);
        Assert.That(result.Value, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Severity == MappingSeverity.Error && d.SourcePath.EndsWith(name)), Is.True);
    }

    [Test]
    public void Export_RejectsInvalidRowsAndContradictoryHeaders()
    {
        foreach (var options in new[]
        {
            new Dictionary<string, object?> { ["headerRows"] = new[] { 0 } },
            new Dictionary<string, object?> { ["commentRows"] = new[] { -1 } },
            new Dictionary<string, object?> { ["header"] = false, ["headerRows"] = new[] { 2 } },
            new Dictionary<string, object?> { ["headerRows"] = Array.Empty<int>() },
            new Dictionary<string, object?> { ["escapeChar"] = "\\" }
        })
            Assert.That(new CsvDialectExporter().Export(Contract(Endpoint(format: new("csv", Options: options))), "csv").IsSuccessful, Is.False);
    }

    [Test]
    public void Export_ReportsUnsupportedOptionsWithoutPrintingTheirValues()
    {
        var options = new Dictionary<string, object?> { ["headerRepeat"] = false, ["customParser"] = "PRIVATE" };
        var result = new CsvDialectExporter().Export(Contract(Endpoint(format: new("csv", Options: options))), "csv");
        Assert.That(result.IsSuccessful, Is.True);
        Assert.That(result.Diagnostics.Count, Is.EqualTo(2));
        Assert.That(result.Diagnostics.All(d => d.Code == "CSV005" && d.Severity == MappingSeverity.Warning), Is.True);
        Assert.That(string.Join(";", result.Diagnostics.Select(d => d.Message)), Does.Not.Contain("PRIVATE"));
        Assert.That(result.Value, Does.Not.Contain("PRIVATE"));
    }

    [Test]
    public void Export_ConsumesDataPackageMappedDialectAndRoundTripsThroughPackageReader()
    {
        var package = new DataPackage { Name = "test", Resources = [new Resource
        {
            Name = "customers", Format = "csv", Paths = [new StubPath("customers.csv")],
            Dialect = new TableDelimitedDialect { Delimiter = ";", LineTerminator = "\n", CommentChar = "#" }
        }] };
        var json = new CsvDialectExporter().Export(package.ToCanonicalContract().RequireValue(), "customers-endpoint");
        Assert.That(json.Diagnostics, Is.Empty);
        var wrapper = new JObject { ["resources"] = new JArray(new JObject
        { ["name"] = "customers", ["path"] = "customers.csv", ["dialect"] = JObject.Parse(json.RequireValue()) }) };
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(wrapper.ToString()));
        var restored = new DataPackageFactory().LoadFromStream(stream);
        Assert.That(restored.Resources.Single().Dialect!.Delimiter, Is.EqualTo(";"));
        Assert.That(restored.Resources.Single().Dialect!.LineTerminator, Is.EqualTo("\n"));
    }

    [Test]
    public void Export_AcceptsJsonOptionValuesAndCsvMediaType()
    {
        var options = JObject.Parse("""{"delimiter":";","header":true,"headerRows":[2]}""")
            .Properties().ToDictionary(p => p.Name, p => (object?)p.Value);
        var endpoint = Endpoint(format: new(null, "text/csv; charset=utf-8", Options: options));
        var result = new CsvDialectExporter().Export(Contract(endpoint), "csv");
        Assert.That(result.IsSuccessful, Is.True);
        Assert.That((string?)JObject.Parse(result.RequireValue())["delimiter"], Is.EqualTo(";"));
    }

    [TestCase(" CSV ", "ignored.bin")]
    [TestCase(".csv", "ignored.bin")]
    [TestCase("csv.gz", "ignored.bin")]
    [TestCase("csv.gzip", "ignored.bin")]
    [TestCase(null, "https://example.test/data.CSV.gz?version=1")]
    [TestCase(null, "data.csv.gzip")]
    public void Export_RecognizesCsvFormatsWithoutOpeningData(string? format, string path)
    {
        var endpoint = Endpoint(format: new(format)) with { Location = new PathLocation([path]), Kind = EndpointKind.ObjectStorage };
        Assert.That(new CsvDialectExporter().Export(Contract(endpoint), "csv").IsSuccessful, Is.True);
    }

    [Test]
    public void CreateDescriptor_ReusesNativeModelAndCopiesMutableRows()
    {
        var rows = new List<int> { 2 };
        var endpoint = Endpoint(format: new("csv", Options: new Dictionary<string, object?> { ["headerRows"] = rows }));
        var descriptor = new CsvDialectExporter().CreateDescriptor(Contract(endpoint), "csv").RequireValue();
        Assert.That(descriptor, Is.InstanceOf<TableDelimitedDialect>());
        descriptor.HeaderRows!.Add(3);
        Assert.That(rows, Is.EqualTo(new[] { 2 }));
    }

    private sealed class StubPath(string value) : Packata.Core.Storage.IPath
    {
        public string Value => value;
        public bool IsFullyQualified => false;
        public Task<Stream> OpenAsync() => throw new InvalidOperationException("Export must not open resource data.");
        public Task<bool> ExistsAsync() => Task.FromResult(false);
    }
}
