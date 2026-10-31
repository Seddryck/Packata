using NUnit.Framework;
using Packata.Application.Generation;
using Packata.LanguageServer.Core;

namespace Packata.LanguageServer.Testing;

public class SchemaGenerationOperationTests
{
    [TestCase("drill")]
    [TestCase("questdb")]
    public async Task Disabled_DDL_dialects_are_not_offered_or_accepted_in_the_editorAsync(string dialect)
    {
        var store = new DocumentStore(); store.Update(Source, Text, 4);
        var operations = new SchemaGenerationOperations(store);
        var options = await operations.OptionsAsync(Source, SchemaGenerationOperations.Key(Text), default);
        Assert.That(options.Catalog.Targets.Single(t => t.Id == "ddl").Dialects!.Select(d => d.Id), Does.Not.Contain(dialect));
        var result = await operations.GenerateAsync(new(Source.AbsoluteUri, SchemaGenerationOperations.Key(Text),
            new(AssetId: "orders", Target: "ddl", Dialect: dialect), 4, "/schema/0"), default);
        Assert.That(result.Result.Content, Is.Null);
        Assert.That(result.Result.Diagnostics.Single().Code, Is.EqualTo("GEN_OPTIONS"));
    }

    private static readonly Uri Source = new("file:///workspace/second/contract.yaml");
    private const string Text = """
        apiVersion: v3.2.0
        kind: DataContract
        id: sales
        schema:
          - name: orders
            properties:
              - name: id
                logicalType: integer
        """;

    [TestCase("avro")]
    [TestCase("spark-json")]
    public async Task Structured_exports_use_unsaved_snapshots_and_selected_assetAsync(string format)
    {
        var store = new DocumentStore(); store.Update(Source, Text, 4);
        var options = new GenerationOptions(AssetId: "orders", Operation: "export", OutputFormat: format);
        var response = await new SchemaGenerationOperations(store).GenerateAsync(new(Source.AbsoluteUri, SchemaGenerationOperations.Key(Text), options, 4, "/schema/0"), default);
        var direct = await new SchemaGenerationService().GenerateAsync(Text, options);
        Assert.That(response.Result.IsSuccessful, Is.True, string.Join(";", response.Result.Diagnostics));
        Assert.That(response.Result.Content, Is.EqualTo(direct.Content));
        Assert.That(response.Version, Is.EqualTo(4));
        store.Update(Source, Text, 5);
        var stale = await new SchemaGenerationOperations(store).GenerateAsync(new(Source.AbsoluteUri, SchemaGenerationOperations.Key(Text), options, 4, "/schema/0"), default);
        Assert.That(stale.Result.Content, Is.Null);
    }

    [Test]
    public async Task Ddl_preserves_snapshot_scope_and_explicit_dialectAsync()
    {
        var store = new DocumentStore(); store.Update(Source, Text, 4);
        var operations = new SchemaGenerationOperations(store);
        var options = new GenerationOptions(AssetId: "orders", Target: "ddl", Language: "sql", Dialect: "sqlserver");
        var result = await operations.GenerateAsync(new(Source.AbsoluteUri, SchemaGenerationOperations.Key(Text), options, 4, "/schema/0"), default);
        var direct = await new SchemaGenerationService().GenerateAsync(Text, options);
        Assert.That(result.Result.IsSuccessful, Is.True, string.Join(";", result.Result.Diagnostics));
        Assert.That(result.Result.Content, Is.EqualTo(direct.Content));
        Assert.That(result.Result.Language, Is.EqualTo("sql"));
    }

    [TestCase("record")]
    [TestCase("datatable")]
    [TestCase("polars")]
    public async Task Synchronized_source_matches_direct_Application_generationAsync(string target)
    {
        var store = new DocumentStore();
        store.Update(Source, Text, 2);
        var operations = new SchemaGenerationOperations(store);
        var discovery = await operations.OptionsAsync(Source, null, default);
        Assert.That(discovery.Catalog, Is.SameAs(SchemaGenerationService.Catalog));
        Assert.That(discovery.Assets.Single().SourcePath, Is.EqualTo("/schema/0"));
        var result = await operations.GenerateAsync(new(Source.AbsoluteUri, discovery.SelectionKey,
            new(AssetId: "orders", Target: target, Language: "csharp"), 2, "/schema/0"), default);
        var direct = await new SchemaGenerationService().GenerateAsync(Text, new(AssetId: "orders", Target: target, Language: "csharp"), baseUri: new Uri(Source, "."));
        Assert.That(result.Result.IsSuccessful, Is.True, string.Join("\n", result.Result.Diagnostics));
        Assert.That(result.Result.Content, Is.EqualTo(direct.Content));
        Assert.That(result.Result.Diagnostics, Is.EqualTo(direct.Diagnostics));
        Assert.That(result.Version, Is.EqualTo(2));
    }

    [Test]
    public async Task Explicit_unsaved_template_snapshot_is_used_instead_of_diskAsync()
    {
        var store = new DocumentStore();
        store.Update(Source, Text, 1);
        var operations = new SchemaGenerationOperations(store);
        var result = await operations.GenerateAsync(new(Source.AbsoluteUri, SchemaGenerationOperations.Key(Text), new(), 1,
            Template: new("file:///workspace/second/bar.template.cs", "{{ model.asset.name }} unsaved", 7)), default);
        Assert.That(result.Result.Content, Is.EqualTo("orders unsaved"));
        Assert.That(result.Result.TemplateVersion, Is.EqualTo(7));
    }

    [Test]
    public async Task Stale_native_or_canonical_selection_never_rendersAsync()
    {
        var store = new DocumentStore();
        store.Update(Source, Text, 2);
        var operations = new SchemaGenerationOperations(store);
        var old = await operations.OptionsAsync(Source, "old-key", default);
        Assert.That(old.Diagnostics.Single().Code, Is.EqualTo("GEN_STALE"));
        var stale = await operations.GenerateAsync(new(Source.AbsoluteUri, "old-key", new(Target: "record", Language: "csharp"), 1), default);
        Assert.That(stale.Result.Content, Is.Null);
        Assert.That(stale.Result.Diagnostics.Single().Code, Is.EqualTo("GEN_STALE"));
        var wrong = await operations.GenerateAsync(new(Source.AbsoluteUri, SchemaGenerationOperations.Key(Text), new(AssetId: "orders", Target: "record", Language: "csharp"), 2, "/schema/9"), default);
        Assert.That(wrong.Result.Diagnostics.Single().Code, Is.EqualTo("GEN_SELECTION"));
    }

    [Test]
    public async Task Source_changes_during_render_discard_the_artifactAsync()
    {
        var store = new DocumentStore();
        store.Update(Source, Text, 1);
        var renderer = new MutatingRenderer(() => store.Update(Source, Text + "\n# changed", 2));
        var operations = new SchemaGenerationOperations(store, new(renderer));
        var result = await operations.GenerateAsync(new(Source.AbsoluteUri, SchemaGenerationOperations.Key(Text), new(Target: "record", Language: "csharp"), 1), default);
        Assert.That(result.Result.Content, Is.Null);
        Assert.That(result.Result.Diagnostics.Single().Code, Is.EqualTo("GEN_STALE"));
    }

    [Test]
    public async Task Invalid_documents_missing_templates_and_mapping_failures_are_findingsAsync()
    {
        var store = new DocumentStore();
        store.Update(Source, "broken: [", 1);
        var operations = new SchemaGenerationOperations(store);
        Assert.That((await operations.OptionsAsync(Source, null, default)).Diagnostics.Single().Code, Is.EqualTo("GEN_DOCUMENT"));
        store.Update(Source, Text, 2);
        var missing = await operations.GenerateAsync(new(Source.AbsoluteUri, SchemaGenerationOperations.Key(Text), new(), 2,
            Template: new("file:///definitely-missing-packata-template.scriban")), default);
        Assert.That(missing.Result.Diagnostics.Single().Code, Is.EqualTo("GEN_ACCESS"));
        var unsupported = Text.Replace("logicalType: integer", "logicalType: unknown");
        store.Update(Source, unsupported, 3);
        var bad = await operations.GenerateAsync(new(Source.AbsoluteUri, SchemaGenerationOperations.Key(unsupported), new(Target: "record", Language: "csharp"), 3), default);
        Assert.That(bad.Result.Content, Is.Null);
        Assert.That(bad.Result.Diagnostics.Any(d => d.Severity == "Error"), Is.True);
        Assert.That(bad.Findings.Single(f => f.Diagnostic.Code == "UnsupportedType").Location?.Uri, Is.EqualTo(Source.AbsoluteUri));
    }

    [Test]
    public async Task Template_locations_are_real_and_do_not_replace_validation_diagnosticsAsync()
    {
        var store = new DocumentStore();
        store.Update(Source, Text, 1);
        var result = await new SchemaGenerationOperations(store).GenerateAsync(new(Source.AbsoluteUri,
            SchemaGenerationOperations.Key(Text), new(), 1, Template: new("file:///workspace/template.scriban", "{{ if }}", 4)), default);
        var finding = result.Findings.Single(f => f.Diagnostic.Code == "GEN_TEMPLATE");
        Assert.That(finding.Location?.Uri, Is.EqualTo("file:///workspace/template.scriban"));
        Assert.That(finding.Location?.Range.Start.Line, Is.Zero);
    }

    [Test]
    public void Cancelled_requests_do_not_publish_content()
    {
        var store = new DocumentStore();
        store.Update(Source, Text, 1);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.That(async () => await new SchemaGenerationOperations(store).GenerateAsync(new(Source.AbsoluteUri,
            SchemaGenerationOperations.Key(Text), new(Target: "record", Language: "csharp"), 1), cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    private sealed class MutatingRenderer(Action mutate) : ISchemaTemplateRenderer
    {
        public Task<string> RenderAsync(TemplateInput template, string engine, IReadOnlyDictionary<string, object?> context, CancellationToken cancellationToken)
        { mutate(); return Task.FromResult("discard me"); }
    }
}
