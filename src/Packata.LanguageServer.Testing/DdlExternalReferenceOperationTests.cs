using NUnit.Framework;
using Packata.LanguageServer.Core;

namespace Packata.LanguageServer.Testing;

public class DdlExternalReferenceOperationTests
{
    [Test]
    public async Task Unsaved_source_buffers_resolve_external_contract_metadata_from_the_document_directoryAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "packata-lsp-ddl-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "members.odcs.yaml");
            await File.WriteAllTextAsync(source, "invalid: [old disk snapshot");
            const string members = """
                {"apiVersion":"v3.2.0","kind":"DataContract","id":"membership","schema":[{"name":"members","properties":[{"name":"country","logicalType":"string","physicalType":"varchar(2)","relationships":[{"to":"countries.odcs.yaml#/schema/countries/properties/iso_code"}]}]}]}
                """;
            const string countries = """
                {"apiVersion":"v3.2.0","kind":"DataContract","id":"lookup","schema":[{"name":"countries","properties":[{"name":"iso_code","logicalType":"string","physicalType":"varchar(2)","primaryKey":true}]}]}
                """;
            await File.WriteAllTextAsync(Path.Combine(directory, "countries.odcs.yaml"), countries);
            var uri = new Uri(source);
            var documents = new DocumentStore();
            documents.Update(uri, members, 4);
            var request = new SchemaGenerationRequest(uri.AbsoluteUri, SchemaGenerationOperations.Key(members),
                new(AssetId: "members", Target: "ddl", Dialect: "sqlserver"), 4, "/schema/0");
            var response = await new SchemaGenerationOperations(documents).GenerateAsync(request, default);
            Assert.That(response.Result.IsSuccessful, Is.True, string.Join(";", response.Result.Diagnostics));
            Assert.That(response.Version, Is.EqualTo(4));
            Assert.That(response.Result.Content, Does.Contain("REFERENCES [countries] ([iso_code])").And.Not.Contain("CREATE TABLE [countries]"));
            Assert.That(await File.ReadAllTextAsync(source), Is.EqualTo("invalid: [old disk snapshot"));
        }
        finally { Directory.Delete(directory, true); }
    }
}
