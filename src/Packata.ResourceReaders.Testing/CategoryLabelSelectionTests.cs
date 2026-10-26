using System.Text;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Testing;

public class CategoryLabelSelectionTests
{
    [TestCase("de", null, "Norden", "Süden", 0)]
    [TestCase("FR-be", null, "Nord belge", "Sud", 0)]
    [TestCase("fr-CA", null, "Nord", "Sud", 0)]
    [TestCase("es", null, "North", "South", 1)]
    [TestCase(null, "german", "Norden", "Süden", 0)]
    [TestCase(null, "short", "N.", "S.", 0)]
    [TestCase(null, null, "North", "South", 0)]
    public async Task Selects_labels_and_reuses_lookup_with_one_diagnostic(string? language, string? label,
        string north, string south, int warnings)
    {
        var streams = new Streams();
        var diagnostics = new List<string>();
        var factory = Factory(streams, language, label, diagnostics);
        using var reader = await factory.OpenAsync(Request());
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo(north));
        Assert.That(reader.GetString(1), Is.EqualTo(south));
        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(string)));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("X"));
        Assert.That(reader.GetString(1), Is.EqualTo("N"), "Native quoted codes remain unchanged.");
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.Empty);
        Assert.That(diagnostics, Has.Count.EqualTo(warnings));
        Assert.That(streams.LookupOpens, Is.EqualTo(1));
    }

    [Test]
    public async Task Explicit_empty_translation_is_preserved_and_missing_translation_uses_code()
    {
        var streams = new Streams { Lookup = "key,title,german,french,belgian,short,description\r\nN,North,,Nord,Nord belge,N.,Texte\r\nS,South,~\r\n" };
        using var reader = await Factory(streams, null, "german").OpenAsync(Request());
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.Empty);
        Assert.That(reader.GetString(1), Is.EqualTo("S"));
    }

    [Test]
    public async Task Language_missing_in_table_without_default_keeps_original_codes()
    {
        var request = Request();
        var lookup = request.Contract!.Assets[1] with { CategoryTable = new(new("key"), labelFields: [new("german")]) };
        request = request with { Contract = request.Contract with { Assets = [request.Contract.Assets[0], lookup] } };
        var diagnostics = new List<string>();
        using var reader = await Factory(new(), "es", null, diagnostics).OpenAsync(request);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("N"));
        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Null_translation_falls_back_to_default_and_duplicate_display_labels_are_allowed()
    {
        var streams = new Streams { Lookup = "key,title,german,french,belgian,short,description\r\nN,North,Norden,~,~,Same,Texte\r\nS,South,Süden,Sud,~,Same,Texte\r\n" };
        var request = Request();
        var lookup = request.Contract!.Assets[1];
        lookup = lookup with { Schema = lookup.Schema! with { Fields = lookup.Schema.Fields.Select(field =>
            field.Name == "french" ? field with { Extensions = Missing() } : field).ToArray() } };
        request = request with { Contract = request.Contract with { Assets = [request.Contract.Assets[0], lookup] } };
        using (var reader = await Factory(streams, "fr-BE", null).OpenAsync(request))
        {
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo("North"));
            Assert.That(reader.GetString(1), Is.EqualTo("Sud"));
        }
        using var alternate = await Factory(streams, null, "short").OpenAsync(request);
        Assert.That(alternate.Read(), Is.True);
        Assert.That(alternate.GetString(0), Is.EqualTo("Same"));
        Assert.That(alternate.GetString(1), Is.EqualTo("Same"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Language_selects_different_column_names_across_tables_while_explicit_selection_requires_each(bool byLanguage)
    {
        var request = Request();
        var contract = request.Contract!;
        var second = contract.Assets[1] with { Id = "second", Name = "second",
            CategoryTable = new(new("key"), new("title"), labelFields: [new("french")]),
            Schema = contract.Assets[1].Schema! with { Fields = contract.Assets[1].Schema!.Fields.Select(field =>
                field.Name == "french" ? field with { Language = "de" } : field with { Language = null }).ToArray() } };
        var schema = request.Schema! with { Fields = [request.Schema!.Fields[0], request.Schema.Fields[1] with
            { ValueDomain = new(reference: new("second")) }] };
        request = request with { Schema = schema, Contract = contract with { Assets = [.. contract.Assets, second] } };
        var factory = Factory(new(), byLanguage ? "de" : null, byLanguage ? null : "german");
        if (!byLanguage)
        {
            Assert.That(async () => await factory.OpenAsync(request),
                Throws.TypeOf<CategoryTableDecodingException>().With.Message.Contains("second").And.Message.Contains("german"));
            return;
        }
        using var reader = await factory.OpenAsync(request);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("Norden"));
        Assert.That(reader.GetString(1), Is.EqualTo("Sud"));
    }

    [TestCase("description", "not a declared")]
    [TestCase("missing", "not a declared")]
    public void Explicit_selection_rejects_missing_or_undeclared_fields(string label, string message)
    {
        var streams = new Streams();
        Assert.That(async () => await Factory(streams, null, label).OpenAsync(Request()),
            Throws.TypeOf<CategoryTableDecodingException>().With.Message.Contains(message));
        Assert.That(streams.LookupOpens, Is.Zero);
    }

    [Test]
    public void Language_ambiguity_requires_explicit_selection()
    {
        var request = Request();
        var lookup = request.Contract!.Assets[1];
        lookup = lookup with { Schema = lookup.Schema! with { Fields = lookup.Schema.Fields.Select(field =>
            field.Name == "short" ? field with { Language = "de" } : field).ToArray() } };
        request = request with { Contract = request.Contract with { Assets = [request.Contract.Assets[0], lookup] } };
        Assert.That(async () => await Factory(new(), "de", null).OpenAsync(request),
            Throws.TypeOf<CategoryTableDecodingException>().With.Message.Contains("--decode-categories-label"));
    }

    [TestCase("label_de", null)]
    [TestCase("fr_XX", null)]
    [TestCase("fr-a", null)]
    [TestCase("", null)]
    [TestCase("fr", "german")]
    [TestCase(null, " ")]
    public void Invalid_factory_selectors_fail_before_opening(string? language, string? label)
        => Assert.That(() => Factory(new(), language, label), Throws.InstanceOf<ArgumentException>());

    [Test]
    public async Task Label_encoded_input_does_not_load_or_translate_lookup()
    {
        var request = Request();
        request = request with { Schema = request.Schema! with { Fields = request.Schema.Fields.Select(field =>
            field with { ValueDomain = new(reference: new("missing", encodedAs: ValueEncoding.Label)) }).ToArray() } };
        var streams = new Streams();
        using var reader = await Factory(streams, null, "nonexistent").OpenAsync(request);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("N"));
        Assert.That(streams.LookupOpens, Is.Zero);
    }

    private static ResourceReaderFactory Factory(Streams streams, string? language, string? label, List<string>? diagnostics = null)
        => ResourceReaderFactory.Create(options =>
        {
            options.DecodeCategories = language is null && label is null;
            options.DecodeCategoriesLanguage = language;
            options.DecodeCategoriesLabel = label;
            options.CategoryDecodingDiagnostic = message => diagnostics?.Add(message);
        }, streams);

    private static DataEndpointReadRequest Request()
    {
        var schema = new DataSchema([
            new("code", "string", ValueDomain: new(reference: new("lookup"))),
            new("other", "string", ValueDomain: new(reference: new("lookup")))]);
        var lookupSchema = new DataSchema([
            new("key", "string"), new("title", "string", Language: "en"),
            new("german", "string", Language: "de", Extensions: Missing()), new("french", "string", Language: "fr"),
            new("belgian", "string", Language: "fr-BE", Extensions: Missing()), new("short", "string"),
            new("description", "string", Language: "fr")]);
        var endpoint = new DataEndpoint("data", null, EndpointKind.File, null, new PathLocation(["data.csv"]), Format: new("csv"));
        var lookupEndpoint = endpoint with { Id = "lookup", Location = new PathLocation(["lookup.csv"]) };
        var asset = new DataAsset("data", "data", null, null, AssetKind.Table, schema, EndpointBindings: [new("data")]);
        var lookup = new DataAsset("lookup", "lookup", null, null, AssetKind.Table, lookupSchema, EndpointBindings: [new("lookup")],
            CategoryTable: new(new("key"), new("title"), labelFields: [new("german"), new("french"), new("belgian"), new("short")]));
        var contract = new DataContract(new("test"), new(), [asset, lookup], [endpoint, lookupEndpoint], new());
        return new(endpoint, schema, Contract: contract);
    }

    private static ExtensionMetadata Missing() => ExtensionMetadata.For("datapackage",
        new Dictionary<string, object?> { ["missingValues"] = new[] { "~" } });

    private sealed class Streams : IEndpointStreamResolver
    {
        public string Lookup { get; init; } = "key,title,german,french,belgian,short,description\r\nN,North,Norden,Nord,Nord belge,N.,Texte\r\nS,South,Süden,Sud,~,S.,Texte sud\r\n";
        public int LookupOpens { get; private set; }
        public ValueTask<Stream> OpenAsync(string path, CancellationToken cancellationToken = default)
        {
            if (path == "lookup.csv") LookupOpens++;
            return ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(path == "lookup.csv" ? Lookup :
                "code,other\r\nN,S\r\nX,\"N\"\r\n,N\r\n")));
        }
    }
}
