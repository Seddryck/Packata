using System.Diagnostics;
using NUnit.Framework;
using Packata.Application.Generation;

namespace Packata.Application.Testing;

public class ModelGenerationTests
{
    private const string Source = """
        apiVersion: v3.2.0
        kind: DataContract
        id: sample
        schema:
          - name: orders
            properties:
              - name: optional
                logicalType: string
                nullable: true
              - name: class
                logicalType: integer
                required: true
                nullable: true
              - name: amount
                logicalType: number
                required: true
                logicalTypeOptions:
                  precision: 18
                  scale: 2
                  minimum: 0
              - name: a-b
                logicalType: string
                required: true
              - name: a_b
                logicalType: string
                required: true
              - name: customer
                logicalType: object
                required: true
                properties:
                  - name: name
                    logicalType: string
                    required: true
              - name: tags
                logicalType: array
                required: true
                items:
                  name: tag
                  logicalType: string
              - name: created
                logicalType: date
                required: true
        """;

    [Test]
    public async Task Ordinary_CSharp_class_compiles_and_preserves_absence_null_and_values()
    {
        var result = await Generate("class", "csharp");
        Assert.That(result.Content, Does.Contain("public class Orders").And.Not.Contain("record Orders"));
        var assembly = SchemaGenerationTests.Compile(result.Content + """

            public static class Fixture
            {
                public static bool Run()
                {
                    var absent = default(OptionalValue<string?>);
                    var presentNull = new OptionalValue<string?>(null);
                    var presentValue = new OptionalValue<string?>("value");
                    var order = new Orders { @class = null, amount = 12.34m, a_b = "a", a_b_2 = "b",
                        customer = new Orders_customer { name = "x" }, tags = new string[] { "tag" }, created = new System.DateOnly(2026, 1, 1) };
                    return !order.optional.HasValue && !absent.HasValue && presentNull.HasValue && presentNull.Value is null
                        && presentValue.Value == "value" && order.@class is null;
                }
            }
            """);
        Assert.That(assembly.GetType("Fixture")!.GetMethod("Run")!.Invoke(null, null), Is.True);
    }

    [TestCase("class")]
    [TestCase("dataclass")]
    [TestCase("pydantic")]
    public async Task Python_models_execute_constructors_nested_collections_and_required_nullable_fields(string target)
    {
        var result = await Generate(target, "python");
        var checks = """

            import inspect
            kwargs = dict(class_=None, amount=Decimal('12.34'), a_b='a', a_b_2='b',
                          customer=Orders_customer(name='buyer'), tags=['tag'], created=date(2026, 1, 1))
            order = Orders(**kwargs)
            assert order.class_ is None
            assert order.customer.name == 'buyer'
            assert order.tags == ['tag']
            assert 'class_' in inspect.signature(Orders).parameters
            try:
                Orders(**{k:v for k,v in kwargs.items() if k != 'class_'})
                raise AssertionError('Required nullable field accepted absence')
            except (TypeError, ValueError):
                pass
            assert Orders(optional=None, **kwargs).optional is None
            assert Orders(optional='present', **kwargs).optional == 'present'
            first, second = Orders(tags=[], **{k:v for k,v in kwargs.items() if k != 'tags'}), Orders(**kwargs)
            first.tags.append('own')
            assert second.tags == ['tag']
            """;
        checks += target == "pydantic" ? """

            assert 'optional' not in order.model_fields_set
            assert 'optional' in Orders(optional=None, **kwargs).model_fields_set
            assert Orders.model_fields['class_'].is_required()
            assert Orders.model_validate({'class': None, 'amount': Decimal('1'), 'a-b': 'a', 'a_b': 'b',
                'customer': {'name':'buyer'}, 'tags': [], 'created': date(2026,1,1)}).a_b_2 == 'a'
            assert Orders.model_validate({'class': None, 'amount': Decimal('1'), 'a-b': 'a', 'a_b': 'b',
                'customer': {'name':'buyer'}, 'tags': [], 'created': date(2026,1,1)}).a_b == 'b'
            for key, invalid in [('class_', '7'), ('amount', Decimal('-1')), ('tags', [42])]:
                try:
                    Orders(**{**kwargs, key: invalid})
                    raise AssertionError('Invalid value accepted: ' + key)
                except ValueError:
                    pass
            """ : """

            assert order.optional is UNSET
            # Plain annotations deliberately do not validate data.
            assert Orders(**{**kwargs, 'class_': 'not an integer'}).class_ == 'not an integer'
            """;
        await ExecutePython(result.Content! + checks);
    }

    [TestCase("csharp-class", "csharp")]
    [TestCase("python-class", "python")]
    [TestCase("python-dataclass", "python")]
    [TestCase("python-pydantic", "python")]
    public async Task Profiles_are_stable_for_custom_templates_and_equivalent_canonical_sources(string profile, string language)
    {
        const string dp = """{"resources":[{"name":"orders","schema":{"fields":[{"name":"id","type":"integer","format":"i64"}]}}]}""";
        const string odcs = """
            apiVersion: v3.2.0
            kind: DataContract
            id: sample
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
        var target = profile == "csharp-class" ? "class" : profile[7..];
        var options = new GenerationOptions(Target: target, Language: language);
        var left = await service.GenerateAsync(dp, options);
        var right = await service.GenerateAsync(odcs, options);
        Assert.That(left.IsSuccessful && right.IsSuccessful, Is.True, string.Join("\n", left.Diagnostics.Concat(right.Diagnostics)));
        Assert.That(left.Content, Is.EqualTo(right.Content));
        var custom = await service.GenerateAsync(odcs, new(Profile: profile),
            new("custom.scriban", "{{ model.target.declarations[0].fields[0].target.type_expression }}"));
        Assert.That(custom.IsSuccessful, Is.True, string.Join("\n", custom.Diagnostics));
        Assert.That(custom.Content, Does.Contain(language == "python" ? "int" : "long"));
        Assert.That(custom.Content, Does.Not.EndWith("\n"), "Custom template whitespace must be preserved.");
    }

    [TestCase("class", "csharp")]
    [TestCase("class", "python")]
    [TestCase("dataclass", "python")]
    [TestCase("pydantic", "python")]
    public async Task Targets_are_deterministic_and_catalogued_with_exact_options(string target, string language)
    {
        var first = await Generate(target, language);
        Assert.That(first.Content, Is.EqualTo((await Generate(target, language)).Content));
        var capability = SchemaGenerationService.Catalog.Targets.Single(t => t.Id == target && t.Language == language);
        Assert.That(capability.Extension, Is.EqualTo(language == "python" ? ".py" : ".cs"));
        Assert.That(capability.Options.Contains("namespace"), Is.EqualTo(language == "csharp"));
        Assert.That(SchemaGenerationService.ValidateOptions(new(Target: target, Language: language, Operation: "export"), null), Is.Not.Null);
        if (language == "python") Assert.That(SchemaGenerationService.ValidateOptions(new(Target: target, Language: language, Namespace: "Invalid"), null), Is.Not.Null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.That(async () => await new SchemaGenerationService().GenerateAsync(Source, new(Target: target, Language: language), cancellationToken: cts.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task Lossy_presence_precision_and_unsupported_constraints_are_diagnosed()
    {
        var service = new SchemaGenerationService();
        var presence = await service.GenerateAsync(Source.Replace("nullable: true", "nullable: false"), new(Target: "pydantic", Language: "python"));
        Assert.That(presence.Content, Is.Null);
        Assert.That(presence.Diagnostics.Any(d => d.Code == "MODEL_PRESENCE" && d.SourcePath.Contains("optional")), Is.True);
        var precision = await service.GenerateAsync(Source.Replace("precision: 18", "precision: 38"), new(Target: "class", Language: "csharp"));
        Assert.That(precision.Content, Is.Null);
        Assert.That(precision.Diagnostics.Any(d => d.Code == "RepresentationLoss" && d.Severity == "Error"), Is.True);
        var python = await service.GenerateAsync(Source.Replace("precision: 18", "precision: 38"), new(Target: "class", Language: "python"));
        Assert.That(python.IsSuccessful, Is.True, string.Join("\n", python.Diagnostics));
        var unsupported = await service.GenerateAsync(Source.Replace("minimum: 0", "minimum: 0\n        unique: true"), new(Target: "pydantic", Language: "python"));
        Assert.That(unsupported.Diagnostics.Any(d => d.Code == "MODEL_CONSTRAINT" && d.Message.Contains("unique")), Is.True);
    }

    private static async Task<GenerationResult> Generate(string target, string language)
    {
        var result = await new SchemaGenerationService().GenerateAsync(Source, new(Target: target, Language: language));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        return result;
    }

    [TestCase("class", "csharp", "Order.cs")]
    [TestCase("class", "python", "order.py")]
    [TestCase("dataclass", "python", "order_dataclass.py")]
    [TestCase("pydantic", "python", "order_model.py")]
    public async Task Complete_documented_examples_match_generation_and_compile_or_execute(string target, string language, string filename)
    {
        var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../examples/schema-models"));
        var source = await File.ReadAllTextAsync(Path.Combine(folder, "contract.odcs.yaml"));
        var example = await File.ReadAllTextAsync(Path.Combine(folder, filename));
        var result = await new SchemaGenerationService().GenerateAsync(source, new(Target: target, Language: language, TypeName: "Order", Namespace: language == "csharp" ? "Example.Models" : null));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Content, Is.EqualTo(example.ReplaceLineEndings("\n")));
        if (language == "csharp") SchemaGenerationTests.Compile(example);
        else await ExecutePython(example + "\nassert Order(id=1, label=None).label is None\n");
    }

    [TestCase("class", "csharp")]
    [TestCase("class", "python")]
    [TestCase("dataclass", "python")]
    [TestCase("pydantic", "python")]
    public async Task Maps_arrays_of_nested_models_and_escaped_literals_are_usable(string target, string language)
    {
        const string source = """
            apiVersion: v3.2.0
            kind: DataContract
            id: nested
            schema:
              - name: sample
                properties:
                  - name: labels
                    logicalType: map
                    required: true
                    map:
                      key:
                        name: key
                        logicalType: string
                      value:
                        name: value
                        logicalType: integer
                        nullable: true
                  - name: lines
                    logicalType: array
                    required: true
                    items:
                      name: line
                      logicalType: object
                      properties:
                        - name: "quote\"slash\\line\n😀"
                          logicalType: string
                          required: true
            """;
        var result = await new SchemaGenerationService().GenerateAsync(source, new(Target: target, Language: language));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        if (language == "csharp") SchemaGenerationTests.Compile(result.Content!);
        else await ExecutePython(result.Content! + "\nassert Sample(labels={'key': None}, lines=[]).labels['key'] is None\n");
        var bad = await new SchemaGenerationService().GenerateAsync(source.ReplaceLineEndings("\n").Replace("name: key\n            logicalType: string", "name: key\n            logicalType: integer"), new(Target: target, Language: language));
        Assert.That(bad.Diagnostics.Any(d => d.Code == "MODEL_MAP_KEY"), Is.True, string.Join("\n", bad.Diagnostics));
    }

    [Test]
    public async Task Pydantic_enums_descriptions_lengths_and_aliases_validate_values()
    {
        const string source = """
            apiVersion: v3.2.0
            kind: DataContract
            id: enum
            schema:
              - name: sample
                properties:
                  - name: model_dump
                    logicalType: string
                    required: true
                    description: 'Quoted "description"'
                    enum:
                      - value: open
                      - value: closed
                  - name: text
                    logicalType: string
                    required: true
                    logicalTypeOptions:
                      minLength: 2
                      maxLength: 4
                  - name: count
                    logicalType: integer
                    required: true
                    logicalTypeOptions:
                      format: u8
            """;
        var result = await new SchemaGenerationService().GenerateAsync(source, new(Target: "pydantic", Language: "python"));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        await ExecutePython(result.Content! + """

            assert Sample.model_validate({'model_dump': 'open', 'text': 'ok', 'count': 255}).field_model_dump == 'open'
            assert Sample.model_fields['field_model_dump'].description == 'Quoted "description"'
            for data in [dict(model_dump='bad', text='ok', count=0), dict(model_dump='open', text='x', count=0), dict(model_dump='open', text='ok', count=256)]:
                try:
                    Sample.model_validate(data)
                    raise AssertionError('Invalid constraints accepted')
                except ValueError:
                    pass
            """);
    }

    private static async Task ExecutePython(string content)
    {
        var python = Environment.GetEnvironmentVariable("PACKATA_TEST_PYTHON");
        if (python is null) { Assert.Ignore("Set PACKATA_TEST_PYTHON to Python 3.10+ with Pydantic 2 for executable model fixtures."); return; }
        var path = Path.Combine(Path.GetTempPath(), "packata-model-" + Guid.NewGuid().ToString("N") + ".py");
        try
        {
            await File.WriteAllTextAsync(path, content);
            var start = new ProcessStartInfo(python) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            start.ArgumentList.Add("-I"); start.ArgumentList.Add(path);
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); throw; }
            Assert.That(process.ExitCode, Is.Zero, await error + await output + "\n" + content);
        }
        finally { File.Delete(path); }
    }
}
