using System.Diagnostics;
using Microsoft.Data.Analysis;
using Microsoft.ML;
using Microsoft.ML.Data;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Packata.Application.Generation;

namespace Packata.Application.Testing;

public class FrameworkGenerationTests
{
    private const string Source = """
        apiVersion: v3.2.0
        kind: DataContract
        id: sales
        schema:
          - name: orders
            properties:
              - name: id
                logicalType: integer
                required: true
                logicalTypeOptions:
                  format: i64
              - name: name
                logicalType: string
                nullable: true
              - name: amount
                logicalType: number
                logicalTypeOptions:
                  format: fp32
        """;

    [TestCase("pyspark", "python")]
    [TestCase("dotnet-dataframe", "csharp")]
    [TestCase("idataview", "csharp")]
    public async Task Equivalent_sources_generate_deterministic_definitions_and_custom_profile_expressions(string target, string language)
    {
        const string dp = """{"resources":[{"name":"orders","schema":{"fields":[{"name":"id","type":"integer","format":"i64","constraints":{"required":true}},{"name":"name","type":"string"},{"name":"amount","type":"number","format":"fp32","constraints":{"required":true}}]}}]}""";
        // Data Package fields are present by definition; required controls nullability.
        var source = Source.Replace("logicalType: number", "logicalType: number\n        required: true");
        var service = new SchemaGenerationService();
        var options = new GenerationOptions(Target: target, Language: language);
        var result = await service.GenerateAsync(source, options);
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Content, Is.EqualTo((await service.GenerateAsync(source, options)).Content));
        var other = await service.GenerateAsync(dp, options);
        Assert.That(other.IsSuccessful, Is.True, string.Join("\n", other.Diagnostics));
        if (target == "dotnet-dataframe")
        {
            var frame = (DataFrame)Create(result.Content!);
            var second = (DataFrame)Create(other.Content!);
            Assert.That(frame.Rows.Count, Is.Zero);
            Assert.That(frame.Columns.Select(c => (c.Name, c.DataType, c.Length)), Is.EqualTo(second.Columns.Select(c => (c.Name, c.DataType, c.Length))));
            Assert.That(frame.Columns.Select(c => c.DataType), Is.EqualTo(new[] { typeof(long), typeof(string), typeof(float) }));
        }
        else if (target == "idataview")
        {
            var schema = (DataViewSchema)Create(result.Content!);
            var second = (DataViewSchema)Create(other.Content!);
            Assert.That(schema.Select(c => (c.Name, c.Type)), Is.EqualTo(second.Select(c => (c.Name, c.Type))));
            Assert.That(schema.Select(c => c.Type), Is.EqualTo(new DataViewType[] { NumberDataViewType.Int64, TextDataViewType.Instance, NumberDataViewType.Single }));
            ReadOnlyMemory<char> metadata = default;
            schema[1].Annotations.GetValue("packata.schema", ref metadata);
            var field = JObject.Parse(metadata.ToString());
            Assert.That((bool)field["nullable"]!, Is.True);
            Assert.That((bool)field["required"]!, Is.False);
        }
        var profile = SchemaGenerationService.Catalog.Targets.Single(t => t.Id == target).Profile;
        var custom = await service.GenerateAsync(source, new(Profile: profile),
            new("custom.scriban", "{{ model.fields[0].target.type_expression }}"));
        Assert.That(custom.IsSuccessful, Is.True, string.Join("\n", custom.Diagnostics));
        Assert.That(custom.Content, Does.Contain(target switch { "pyspark" => "S.LongType()", "idataview" => "NumberDataViewType.Int64", _ => "PrimitiveDataFrameColumn<long>" }));
    }

    [TestCase("dotnet-dataframe")]
    [TestCase("idataview")]
    public async Task Unsupported_nested_temporal_and_lossy_decimal_mappings_fail_without_partial_code(string target)
    {
        var service = new SchemaGenerationService();
        foreach (var field in new[] { "logicalType: date", "logicalType: object", "logicalType: number\n        logicalTypeOptions:\n          precision: 35\n          scale: 2" })
        {
            var result = await service.GenerateAsync(Source[..Source.IndexOf("      - name: id", StringComparison.Ordinal)] + "      - name: invalid\n        " + field,
                new(Target: target, Language: "csharp"));
            Assert.That(result.Content, Is.Null);
            Assert.That(result.Diagnostics.Any(d => d.Code == "GEN_FRAMEWORK_TYPE" && d.SourcePath == "orders/invalid"), Is.True, string.Join("\n", result.Diagnostics));
        }
    }

    [TestCase("i8", typeof(sbyte))]
    [TestCase("u8", typeof(byte))]
    [TestCase("i16", typeof(short))]
    [TestCase("u16", typeof(ushort))]
    [TestCase("i32", typeof(int))]
    [TestCase("u32", typeof(uint))]
    [TestCase("i64", typeof(long))]
    [TestCase("u64", typeof(ulong))]
    [TestCase("fp32", typeof(float))]
    [TestCase("fp64", typeof(double))]
    public async Task Framework_numeric_scalar_widths_compile_and_retain_raw_types(string format, Type expected)
    {
        var source = Source.Replace("format: i64", "format: " + format);
        if (format.StartsWith("fp")) source = source.Replace("logicalType: integer", "logicalType: number");
        foreach (var target in new[] { "dotnet-dataframe", "idataview" })
        {
            var result = await new SchemaGenerationService().GenerateAsync(source, new(Target: target, Language: "csharp"));
            Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
            var actual = Create(result.Content!);
            Assert.That(actual is DataFrame frame ? frame.Columns[0].DataType : ((DataViewSchema)actual)[0].Type.RawType, Is.EqualTo(expected));
        }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Requiredness_and_nullability_remain_independent_in_every_context(bool required, bool nullable)
    {
        var source = Source.Replace("required: true", $"required: {required.ToString().ToLowerInvariant()}\n        nullable: {nullable.ToString().ToLowerInvariant()}");
        foreach (var (target, language) in new[] { ("pyspark", "python"), ("dotnet-dataframe", "csharp"), ("idataview", "csharp") })
        {
            var result = await new SchemaGenerationService().GenerateAsync(source, new(Target: target, Language: language));
            Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
            var field = JObject.FromObject(result.Context!)["fields"]![0]!;
            Assert.That((bool)field["required"]!, Is.EqualTo(required));
            Assert.That((bool)field["nullable"]!, Is.EqualTo(nullable));
        }
    }

    [Test]
    public async Task PySpark_unicode_and_escaped_names_are_importable_and_preserved()
    {
        var source = Source.Replace("name: name", "name: 'quote\"slash\\line😀'");
        var service = new SchemaGenerationService();
        var result = await service.GenerateAsync(source, new(Target: "pyspark", Language: "python", TypeName: "Orders"));
        var exported = await service.GenerateAsync(source, new(Operation: "export", OutputFormat: "spark-json"));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        await Python(result.Content!, exported.Content!);
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("18446744073709551616")]
    public async Task Invalid_key_cardinalities_fail_before_rendering(string cardinality)
    {
        var source = Source.Replace("format: i64", "format: u32\n        customProperties:\n          - property: mlnet\n            value:\n              keyCount: " + cardinality);
        var result = await new SchemaGenerationService().GenerateAsync(source, new(Target: "idataview", Language: "csharp"));
        Assert.That(result.Content, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Code == "GEN_FRAMEWORK_TYPE" && d.SourcePath == "orders/id"), Is.True);
    }

    [Test]
    public async Task Dataframe_decimal_storage_and_identifier_collisions_compile()
    {
        var source = Source.Replace("format: fp32", "precision: 12\n          scale: 2").Replace("name: name", "name: 'quote\"slash\\line'");
        var result = await new SchemaGenerationService().GenerateAsync(source,
            new(Target: "dotnet-dataframe", Language: "csharp", TypeName: "DataFrame", Namespace: "Example"));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        var frame = (DataFrame)Create(result.Content!, "Example.DataFrame_2");
        Assert.That(frame.Columns[1].Name, Is.EqualTo("quote\"slash\\line"));
        Assert.That(frame.Columns[2].DataType, Is.EqualTo(typeof(decimal)));
    }

    [TestCase("[2, 3]", 6)]
    [TestCase("[0]", 0)]
    public async Task Mlnet_vectors_keys_and_annotations_compile_and_preserve_dimensions(string dimensions, int size)
    {
        var source = Source + """

              - name: features
                logicalType: array
                customProperties:
                  - property: mlnet
                    value:
                      dimensions: DIMENSIONS
                items:
                  name: value
                  logicalType: number
                  logicalTypeOptions:
                    format: fp64
              - name: category
                logicalType: integer
                logicalTypeOptions:
                  format: u32
                customProperties:
                  - property: mlnet
                    value:
                      keyCount: 7
        """.Replace("DIMENSIONS", dimensions);
        var result = await new SchemaGenerationService().GenerateAsync(source, new(Target: "idataview", Language: "csharp"));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        var schema = (DataViewSchema)Create(result.Content!);
        var vector = (VectorDataViewType)schema[3].Type;
        Assert.That(vector.Size, Is.EqualTo(size));
        Assert.That(vector.Dimensions, Is.EqualTo(JArray.Parse(dimensions).Values<int>()));
        Assert.That(vector.ItemType, Is.EqualTo(NumberDataViewType.Double));
        Assert.That(((KeyDataViewType)schema[4].Type).Count, Is.EqualTo(7));
    }

    [TestCase("dimensions: [-1]")]
    [TestCase("dimensions: [2147483647, 2]")]
    [TestCase("dimensions: []")]
    [TestCase("dimensions: [1.5]")]
    [TestCase("unknown: true")]
    public async Task Invalid_vector_options_are_field_specific_errors(string declaration)
    {
        var source = Source + "\n      - name: features\n        logicalType: array\n        customProperties:\n          - property: mlnet\n            value:\n              " + declaration + "\n        items:\n          name: value\n          logicalType: integer\n";
        var result = await new SchemaGenerationService().GenerateAsync(source, new(Target: "idataview", Language: "csharp"));
        Assert.That(result.Content, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Code == "GEN_FRAMEWORK_TYPE" && d.SourcePath == "orders/features"), Is.True, string.Join("\n", result.Diagnostics));
    }

    [Test]
    public async Task Pyspark_nested_decimal_and_temporal_schema_matches_spark_json()
    {
        var source = SchemaGenerationTests.Descriptor.Replace("format: u64", "format: i64").Replace("precision: 18", "precision: 12").Replace("timezone: false", "timezone: true");
        var service = new SchemaGenerationService();
        var result = await service.GenerateAsync(source, new(Target: "pyspark", Language: "python", TypeName: "Orders"));
        Assert.That(result.IsSuccessful, Is.True, string.Join("\n", result.Diagnostics));
        Assert.That(result.Content, Does.Contain("S.DecimalType(12,2)").And.Contain("S.ArrayType(").And.Contain("S.TimestampType()"));
        var custom = await service.GenerateAsync(source, new(Profile: "python-pyspark"),
            new("custom.scriban", "{{ model.fields[8].items.target.type_expression }}"));
        Assert.That(custom.Content, Is.EqualTo("S.StringType()"));
        var exported = await service.GenerateAsync(source, new(Operation: "export", OutputFormat: "spark-json"));
        await Python(result.Content!, exported.Content!);
    }

    [TestCase("format: u64")]
    [TestCase("precision: 50\n          scale: 2")]
    public async Task Pyspark_uses_exporter_loss_diagnostics(string options)
    {
        var source = Source.Replace("format: i64", options).Replace("logicalType: integer", options.StartsWith("precision") ? "logicalType: number" : "logicalType: integer");
        var result = await new SchemaGenerationService().GenerateAsync(source, new(Target: "pyspark", Language: "python"));
        Assert.That(result.Content, Is.Null);
        Assert.That(result.Diagnostics.Any(d => d.Severity == "Error" && d.SourcePath == "orders/id"), Is.True);
    }

    private static object Create(string content, string name = "Orders") => SchemaGenerationTests.Compile(content).GetType(name)!.GetMethod("Create")!.Invoke(null, null)!;

    private static async Task Python(string content, string expected)
    {
        var python = Environment.GetEnvironmentVariable("PACKATA_TEST_PYSPARK_PYTHON");
        if (python is null) { Assert.Ignore("Set PACKATA_TEST_PYSPARK_PYTHON to Python with PySpark 3.5+ for interoperability."); return; }
        var start = new ProcessStartInfo(python) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(content + "\nassert Orders.jsonValue() == json.loads(" + Newtonsoft.Json.JsonConvert.ToString(expected) + ")\n");
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero, await error);
    }
}
