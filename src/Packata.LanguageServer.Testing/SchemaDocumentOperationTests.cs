using NUnit.Framework;
using Packata.LanguageServer.Core;

namespace Packata.LanguageServer.Testing;

public class SchemaDocumentOperationTests
{
    private static readonly Uri Source = new("file:///workspace/schema.yaml");
    private const string Text = "resources:\n  - name: orders\n    schema:\n      fields:\n        - name: id\n          type: integer\n";

    [Test]
    public async Task Binary_export_uses_unsaved_snapshot_and_explicit_artifact_representationAsync()
    {
        var store = new DocumentStore();
        store.Update(Source, Text, 4);
        var operations = new SchemaDocumentOperations(store);
        var discovery = await operations.OptionsAsync(Source, default);
        Assert.That(discovery.Formats.Single(f => f.Id == "arrow-ipc").Representation, Is.EqualTo("binary"));
        Assert.That(discovery.Formats.Single(f => f.Id == "arrow-ipc").Profiles, Is.Empty);
        var result = await operations.ExportAsync(new(Source.AbsoluteUri, discovery.SelectionKey, new("arrow-ipc", "orders"), 4, "/resources/0"), default);
        Assert.That(result.Result.IsSuccessful, Is.True, string.Join("\n", result.Result.Diagnostics));
        Assert.That(result.Result.Content, Is.Null);
        Assert.That(result.Result.Bytes, Is.Not.Empty);
        Assert.That(result.Version, Is.EqualTo(4));
    }

    [Test]
    public async Task Stale_mismatched_selection_and_cancellation_never_return_binaryAsync()
    {
        var store = new DocumentStore();
        store.Update(Source, Text, 1);
        var operations = new SchemaDocumentOperations(store);
        var discovery = await operations.OptionsAsync(Source, default);
        var wrong = await operations.ExportAsync(new(Source.AbsoluteUri, discovery.SelectionKey, new("arrow-ipc", "orders"), 1, "/resources/9"), default);
        Assert.That(wrong.Result.Diagnostics.Single().Code, Is.EqualTo("EXPORT_SELECTION"));
        Assert.That(wrong.Result.Bytes, Is.Null);
        store.Update(Source, Text + "# changed\n", 2);
        var stale = await operations.ExportAsync(new(Source.AbsoluteUri, discovery.SelectionKey, new("arrow-ipc", "orders"), 1), default);
        Assert.That(stale.Result.Diagnostics.Single().Code, Is.EqualTo("EXPORT_STALE"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Assert.That(async () => await operations.ExportAsync(new(Source.AbsoluteUri, discovery.SelectionKey, new("arrow-ipc", "orders"), 1), cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
    }
}
