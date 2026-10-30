using System.Data;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Packata.Application.Generation;
using Packata.Core;

namespace Packata.Application.Testing;

public class SchemaGenerationTests
{
    public const string Descriptor = """
        apiVersion: v3.2.0
        kind: DataContract
        id: sales
        version: 1.0.0
        status: draft
        schema:
          - name: orders
            properties:
              - name: id
                logicalType: integer
                required: true
                logicalTypeOptions:
                  format: u64
              - name: amount
                logicalType: number
                logicalTypeOptions:
                  precision: 18
                  scale: 2
              - name: class
                logicalType: string
                required: true
                nullable: true
              - name: a-b
                logicalType: string
              - name: a_b
                logicalType: string
              - name: created
                logicalType: date
              - name: occurred
                logicalType: timestamp
                logicalTypeOptions:
                  timezone: false
                  precision: 6
              - name: customer
                logicalType: object
                properties:
                  - name: name
                    logicalType: string
              - name: tags
                logicalType: array
                items:
                  name: tag
                  logicalType: string
        """;

    [Test]
    public void Catalog_shares_profiles_engines_and_embedded_template_contract()
    {
        Assert.That(SchemaGenerationService.Catalog.Targets.Select(t => t.Id), Is.EqualTo(new[] { "record", "datatable", "polars" }));
        Assert.That(SchemaGenerationService.Catalog.Engines, Does.Contain("scriban").And.Contain("hbs"));
        Assert.That(SchemaGenerationService.Catalog.ContextVersion, Is.EqualTo(1));
    }

    [TestCase("record")]
    [TestCase("datatable")]
    [TestCase("polars")]
    public async Task Builtins_generate_compilable_CSharp_without_executing_Polars(string target)
    {
        var result = await new SchemaGenerationService().GenerateAsync(Descriptor, new(Target: target, Namespace: "Example.Generated", TypeName: "Orders"));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Language, Is.EqualTo("csharp"));
        Assert.That(result.SuggestedFilename, Is.EqualTo("Orders.cs"));
        var assembly = Compile(result.Content!);
        if (target == "datatable")
        {
            using var table = (DataTable)assembly.GetType("Example.Generated.Orders")!.GetMethod("Create")!.Invoke(null, null)!;
            Assert.That(table.Columns["id"]!.DataType, Is.EqualTo(typeof(ulong)));
            Assert.That(table.Columns["id"]!.AllowDBNull, Is.False);
            Assert.That(table.Columns["class"]!.AllowDBNull, Is.True);
            Assert.That(table.Columns["class"]!.ExtendedProperties["packata.required"], Is.True);
            Assert.That(table.Columns["amount"]!.ExtendedProperties["packata.precision"], Is.EqualTo(18));
            Assert.That(table.Columns["amount"]!.ExtendedProperties["packata.scale"], Is.EqualTo(2));
        }
        if (target == "record")
        {
            Assert.That(result.Content, Does.Contain("required ulong id").And.Contain("required string? @class"));
            Assert.That(result.Content, Does.Contain(" a_b ").And.Contain(" a_b_2 "));
            Assert.That(result.Content, Does.Contain("JsonPropertyName(\"a-b\")").And.Contain("Orders_customer"));
        }
        if (target == "polars")
            Assert.That(result.Content, Does.Contain("DataType.Decimal(18, 2)").And.Contain("TimeUnit.Microseconds").And.Contain("DataType.Struct").And.Contain("DataType.List"));
    }

    [TestCase("custom.template.cs", null, "{{ model.asset.name }} {{ model.variables.count }}")]
    [TestCase("custom.scriban", null, "{{ model.asset.name }} {{ model.variables.count }}")]
    [TestCase("custom.hbs", null, "{{model.asset.name}} {{model.variables.count}}")]
    [TestCase("custom.hbs", "scriban", "{{ model.asset.name }} {{ model.variables.count }}")]
    public async Task Custom_templates_use_engine_override_suffix_or_default(string identity, string? engine, string text)
    {
        var result = await new SchemaGenerationService().GenerateAsync(Descriptor, new(Engine: engine, Defines: ["count=42"]), new(identity, text));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Content, Is.EqualTo("orders 42"));
        Assert.That(result.Context!["context_version"], Is.EqualTo(1));
    }

    [Test]
    public async Task Mapping_failures_do_not_render_and_Polars_capacity_is_independent_of_CLR()
    {
        var source = Descriptor.Replace("precision: 18", "precision: 38");
        var service = new SchemaGenerationService(new RejectRenderer());
        var failed = await service.GenerateAsync(source, new(Target: "record"));
        Assert.That(failed.IsSuccessful, Is.False);
        Assert.That(failed.Content, Is.Null);
        Assert.That(failed.Diagnostics.Any(d => d.Code == "RepresentationLoss" && d.Target == "Runtime" && d.Severity == "Error"), Is.True);
        var polars = await new SchemaGenerationService().GenerateAsync(source, new(Target: "polars"));
        Assert.That(polars.IsSuccessful, Is.True, string.Join("\n", polars.Diagnostics));
        Assert.That(polars.Content, Does.Contain("DataType.Decimal(38, 2)"));
        Compile(polars.Content!);
    }

    [Test]
    public async Task Selection_options_template_failures_and_invalid_documents_are_structured()
    {
        var service = new SchemaGenerationService();
        var conflict = await service.GenerateAsync(Descriptor, new(Target: "record"), new("test.scriban", "hello"));
        Assert.That(conflict.Diagnostics[0].Code, Is.EqualTo("GEN_OPTIONS"));
        var duplicate = await service.GenerateAsync(Descriptor, new(Target: "record", Defines: ["x=1", "x=2"]));
        Assert.That(duplicate.Diagnostics[0].Code, Is.EqualTo("GEN_OPTIONS"));
        var bad = await service.GenerateAsync("broken: [", new(Target: "record"));
        Assert.That(bad.Diagnostics[0].Code, Is.EqualTo("GEN_DOCUMENT"));
        var missing = await service.GenerateAsync(Descriptor, new(AssetId: "absent", Target: "record"));
        Assert.That(missing.Diagnostics.Any(d => d.Code == "GEN_ASSET"), Is.True);
        var template = await service.GenerateAsync(Descriptor, new(), new("broken.scriban", "{{ if }}"));
        Assert.That(template.Content, Is.Null);
        Assert.That(template.Diagnostics.Any(d => d.Code == "GEN_TEMPLATE" && d.Template == "broken.scriban"), Is.True);
    }

    [Test]
    public async Task Equivalent_ODCS_and_DP_schemas_produce_equivalent_records_and_never_expose_endpoints()
    {
        const string dp = """{"name":"sales","resources":[{"name":"orders","path":"mysql://user:password@host/db","schema":{"fields":[{"name":"id","type":"integer","format":"i64"}]}}]}""";
        const string odcs = """
            apiVersion: v3.2.0
            kind: DataContract
            id: sales
            schema:
              - name: orders
                properties:
                  - name: id
                    logicalType: integer
                    required: true
                    nullable: true
                    logicalTypeOptions:
                      format: i64
            """;
        var service = new SchemaGenerationService();
        var left = await service.GenerateAsync(dp, new(Target: "record"));
        var right = await service.GenerateAsync(odcs, new(Target: "record"));
        Assert.That(left.IsSuccessful && right.IsSuccessful, Is.True, string.Join("\n", left.Diagnostics.Concat(right.Diagnostics)));
        Assert.That(left.Content, Is.EqualTo(right.Content));
        Assert.That(ContractJson.Serialize(left.Context!), Does.Not.Contain("mysql://"));
    }

    [Test]
    public void Cancellation_before_preparation_is_not_a_generation_failure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That(async () => await new SchemaGenerationService().GenerateAsync(Descriptor, new(Target: "record"), cancellationToken: cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task Cancellation_during_noninterruptible_render_discards_output()
    {
        using var cancellation = new CancellationTokenSource();
        var renderer = new CancellingRenderer(cancellation);
        Assert.That(async () => await new SchemaGenerationService(renderer).GenerateAsync(Descriptor, new(Target: "record"), cancellationToken: cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        await Task.CompletedTask;
    }

    private sealed class RejectRenderer : ISchemaTemplateRenderer
    {
        public Task<string> RenderAsync(TemplateInput template, string engine, IReadOnlyDictionary<string, object?> context, CancellationToken cancellationToken)
            => throw new AssertionException("Mapping failure must not invoke rendering.");
    }

    [TestCase("record", "System")]
    [TestCase("datatable", "Create")]
    [TestCase("polars", "DataType")]
    public async Task Generated_names_avoid_target_import_and_synthesized_member_collisions(string target, string name)
    {
        var source = Descriptor.Replace("name: id", "name: Clone").Replace("name: a-b", "name: ToString");
        var result = await new SchemaGenerationService().GenerateAsync(source, new(Target: target, TypeName: name));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        Compile(result.Content!);
        if (target == "record") Assert.That(result.Content, Does.Contain("Clone_2").And.Contain("ToString_2"));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Generated_targets_preserve_all_required_nullable_combinations(bool required, bool nullable)
    {
        var source = $"""
            apiVersion: v3.2.0
            kind: DataContract
            id: sample
            schema:
              - name: orders
                properties:
                  - name: value
                    logicalType: integer
                    required: {required.ToString().ToLowerInvariant()}
                    nullable: {nullable.ToString().ToLowerInvariant()}
            """;
        var service = new SchemaGenerationService();
        var tableCode = await service.GenerateAsync(source, new(Target: "datatable"));
        Assert.That(tableCode.IsSuccessful, Is.True, string.Join("\n", tableCode.Diagnostics));
        using var table = (DataTable)Compile(tableCode.Content!).GetType("Orders")!.GetMethod("Create")!.Invoke(null, null)!;
        Assert.That(table.Columns[0].DataType, Is.EqualTo(typeof(int)));
        Assert.That(table.Columns[0].AllowDBNull, Is.EqualTo(nullable));
        Assert.That(table.Columns[0].ExtendedProperties["packata.required"], Is.EqualTo(required));
        var record = await service.GenerateAsync(source, new(Target: "record"));
        Assert.That(record.Content, Does.Contain("public " + (required ? "required " : "") + "int" + (nullable ? "?" : "") + " value"));
        Compile(record.Content!);
    }

    [Test]
    public async Task Categories_remain_codes_with_labels_and_ambiguity_is_actionable()
    {
        const string source = """
            apiVersion: v3.2.0
            kind: DataContract
            id: categories
            schema:
              - name: orders
                properties:
                  - name: code
                    logicalType: integer
                    enum:
                      - value: 1
                        label: Low
                      - value: 2
                        label: High
            """;
        var service = new SchemaGenerationService();
        var result = await service.GenerateAsync(source, new(Target: "record"));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(ContractJson.Serialize(result.Context!), Does.Contain("Low").And.Contain("allowed_values"));
        Assert.That(result.Content, Does.Contain("int code").And.Not.Contain("enum "));
        var multiple = source + "\n  - name: another\n    properties:\n      - name: id\n        logicalType: string\n";
        var ambiguous = await service.GenerateAsync(multiple, new(Target: "record"));
        Assert.That(ambiguous.Content, Is.Null);
        Assert.That(ambiguous.Diagnostics.Any(d => d.Code == "GEN_ASSET" && d.Message.Contains("--asset")), Is.True);
    }

    private sealed class CancellingRenderer(CancellationTokenSource cancellation) : ISchemaTemplateRenderer
    {
        public Task<string> RenderAsync(TemplateInput template, string engine, IReadOnlyDictionary<string, object?> context, CancellationToken cancellationToken)
        { cancellation.Cancel(); return Task.FromResult("discard me"); }
    }

    private static Assembly Compile(string content)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(Directory.GetFiles(AppContext.BaseDirectory, "Polars*.dll")).Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create("Generated" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(content, new CSharpParseOptions(LanguageVersion.CSharp11))],
            paths.Select(p => MetadataReference.CreateFromFile(p)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics) + "\n" + content);
        return Assembly.Load(stream.ToArray());
    }
}
