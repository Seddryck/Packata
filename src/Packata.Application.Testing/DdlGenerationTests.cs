using NUnit.Framework;
using Packata.Application.Generation;
using Packata.Core.Contracts;
using Packata.Provisioners.Database;
namespace Packata.Application.Testing;
public class DdlGenerationTests
{
    private const string Descriptor = """
        resources:
          - name: orders
            schema:
              fields:
                - name: id
                  type: integer
                  constraints:
                    required: true
                - name: label
                  type: string
                  x-packata-physicalType: varchar(80)
              primaryKey: [id]
          - name: other
            schema:
              fields:
                - name: id
                  type: integer
        """;
    public static IEnumerable<string> Dialects => OfflineSqlDialects.Catalog.Select(d => d.Id);
    [TestCaseSource(nameof(Dialects))]
    public async Task Every_advertised_dialect_renders_offline_deterministically(string dialect)
    {
        var service = new SchemaGenerationService();
        var options = new GenerationOptions(Target: "ddl", Dialect: dialect);
        var first = await service.GenerateAsync(Descriptor, options);
        var second = await service.GenerateAsync(Descriptor, options with { Language = "sql" });
        Assert.That(first.IsSuccessful, Is.True, string.Join(";", first.Diagnostics));
        Assert.That(first.Content, Does.Contain("CREATE TABLE"));
        Assert.That(first.Content, Does.Contain("orders").And.Contain("other"));
        Assert.That(second.Content, Is.EqualTo(first.Content));
        var document = ContractDocument.Parse(Descriptor);
        var plan = new RelationalSchemaPlanner(OfflineSqlDialects.Resolve(dialect)).PlanSchema(document.Map().RequireValue());
        Assert.That(first.Content, Is.EqualTo(plan.Script.ReplaceLineEndings("\n")));
        var selected = await service.GenerateAsync(document, options with { AssetId = "orders" });
        Assert.That(selected.Content, Does.Contain("orders").And.Not.Contain("other"));
    }
    [Test]
    public async Task Invalid_types_and_cancelled_operations_publish_no_content()
    {
        var service = new SchemaGenerationService();
        var bad = await service.GenerateAsync(Descriptor.Replace("type: integer", "type: vector"), new(Target: "ddl", Dialect: "sqlite"));
        Assert.That(bad.IsSuccessful, Is.False);
        Assert.That(bad.Content, Is.Null);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThatAsync(async () => await service.GenerateAsync(Descriptor, new(Target: "ddl", Dialect: "sqlite"), cancellationToken: cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        foreach (var options in new GenerationOptions[] { new(Target: "ddl"), new(Target: "ddl", Dialect: "missing"), new(Target: "ddl", Dialect: "sqlite", Language: "csharp") })
            Assert.That(SchemaGenerationService.ValidateOptions(options, null), Is.Not.Null);
    }
}
