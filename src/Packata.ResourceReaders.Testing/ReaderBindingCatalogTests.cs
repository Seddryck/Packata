using System.Data;
using System.Text.Json;
using NUnit.Framework;
using Packata.Core.Contracts;
using Packata.Core.Reading;

namespace Packata.ResourceReaders.Testing;

public class ReaderBindingCatalogTests
{
    private const string ReaderKey = "packata.test-reader";

    [Test]
    public void Load_rejects_duplicate_contributions_in_deterministic_file_order()
    {
        using var files = new ManifestDirectory();
        files.Write("z.readerbindings.json", Contribution("test.binding", "Unavailable"));
        files.Write("a.readerbindings.json", Contribution("test.binding", "Unavailable"));
        var catalog = new ReaderBindingCatalog(files.Path);

        Assert.That(() => catalog.Load(ReaderKey, [new("test", _ => null)]),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("duplicate").And.Message.Contains("a.readerbindings.json, z.readerbindings.json"));
    }

    [Test]
    public void Load_ignores_inactive_contributions_and_reports_missing_dependencies_for_active_profiles()
    {
        using var files = new ManifestDirectory();
        files.Write("integration.readerbindings.json", Contribution("test.binding", "Missing.Packata.Binding.Package"));
        var catalog = new ReaderBindingCatalog(files.Path);

        Assert.That(catalog.Load(ReaderKey, []), Is.Empty);
        Assert.That(() => catalog.Load(ReaderKey, [new("test", _ => null)]),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("test.binding")
                .And.Message.Contains("Missing.Packata.Binding.Package").And.Message.Contains(ReaderKey));
    }

    [Test]
    public void Multiple_independent_native_bindings_are_ambiguous_for_the_same_field()
    {
        using var files = new ManifestDirectory();
        var type = typeof(TextBinding);
        var assembly = type.Assembly.GetName().Name;
        var entries = new[] { "first", "second" }.Select(id => new
        {
            id, readerKey = ReaderKey, profileName = "test", assemblyName = assembly, providerType = type.FullName
        });
        files.Write("bindings.readerbindings.json", JsonSerializer.Serialize(entries));
        var catalog = new ReaderBindingCatalog(files.Path);
        var schema = new DataSchema([new("value", "string")]);

        Assert.That(() => ReaderBindingPlan.Create(new CatalogReader(), schema, [new("test", _ => typeof(string))], [], catalog),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("Multiple native bindings").And.Message.Contains("value"));
    }

    private static string Contribution(string id, string assembly) => $$"""
        [
          {
            "id": "{{id}}",
            "readerKey": "{{ReaderKey}}",
            "profileName": "test",
            "assemblyName": "{{assembly}}",
            "providerType": "Example.BindingProvider"
          }
        ]
        """;

    public sealed class TextBinding : IReaderBindingProvider
    {
        public ReaderFieldBinding? TryBind(ReaderRepresentationRequest request) => new(typeof(string));
    }

    private sealed class CatalogReader : IDataEndpointReaderProvider
    {
        public string BindingKey => ReaderKey;

        public bool CanHandle(DataEndpointReadRequest request, ResolvedDataFormat format) => true;

        public ValueTask<IDataReader> OpenAsync(ReaderOpenContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Binding must be resolved before the reader opens.");
    }

    private sealed class ManifestDirectory : IDisposable
    {
        internal string Path { get; } = Directory.CreateTempSubdirectory("packata-bindings-").FullName;
        internal void Write(string file, string text) => File.WriteAllText(System.IO.Path.Combine(Path, file), text);
        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(Path))
                File.Delete(file);
            Directory.Delete(Path);
        }
    }
}
