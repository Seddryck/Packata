using Didot.Core;

namespace Packata.Application.Generation;

public static class GenerationCatalogService
{
    public static GenerationCatalog Catalog { get; } = new(1, 1,
    [
        new("ddl", "SQL DDL", "", "", "sql", false, "Metadata-only SQL; unsupported constraints are reported. Foreign keys outside a selection are errors.",
            Category: "table-definition", Representation: "SQL", Extension: ".sql", ApplicableOptions: ["asset", "dialect", "output", "constraints", "no-descriptions"],
            Dialects: Packata.Provisioners.Database.OfflineSqlDialects.Catalog.Select(d => new SchemaDialect(d.Id, d.Label)).ToArray(), WholeContract: true),
        new("avro", "Avro", "", "", "json", false, "Avro 1.12; compatible string enums, null-first unions, decimal bytes; other constraints are descriptive metadata.",
            "export", "schema-document", "JSON", ".avsc", ["asset", "output", "schema-name", "schema-namespace"], []),
        new("spark-json", "Spark JSON", "", "", "json", false, "Spark 3.x StructType JSON; decimal precision is limited to 38; constraints are descriptive field metadata.",
            "export", "schema-document", "JSON", ".spark.schema.json", ["asset", "output"], []),
        new("json-schema", "JSON Schema", "", "", "json", false, "Draft 2020-12; preserves canonical JSON Schema semantics.",
            "export", "schema-document", "JSON", ".schema.json", ["asset"], []),
        new("record", "C# record", "csharp-record", "scriban", "csharp", true,
            "C# 11 required properties and nullable annotations describe initialization; they do not validate dataset values."),
        new("datatable", "DataTable", "csharp-datatable", "scriban", "csharp", true,
            "AllowDBNull is enforced; requiredness, constraints, categories and parameters are retained as metadata."),
        new("polars", "Polars.NET", "csharp-polars", "scriban", "csharp", true,
            "Polars.NET 1.0.1; schema types do not enforce field requiredness, nullability or contract quality rules."),
        new("arrow", "Arrow Schema (C#)", "csharp-arrow", "scriban", "csharp", true,
            "Apache.Arrow 23.0.0; presence, uniqueness and quality rules require separate validation."),
        new("arrow", "Arrow Schema (Python)", "python-arrow", "scriban", "python", false,
            "Python 3.10+ and PyArrow 23+; presence, uniqueness and quality rules require separate validation.")
    ], ["csharp-record", "csharp-datatable", "csharp-polars", "csharp-arrow", "python-arrow"],
        TemplateEngineFactory.Default.AllSupportedExtensions().Select(s => s.TrimStart('.')).Order().ToArray(), "scriban",
    ["Generation reads descriptor metadata only; it never reads rows, compiles or executes generated code.",
     "Didot rendering is synchronous and cannot be interrupted once entered; cancelled results are discarded.",
     "Only the supplied template is available; filesystem/network includes are not registered."]);

    internal static string Engine(TemplateInput template, string? requested)
    {
        if (requested is not null)
        {
            var engine = requested.Trim().TrimStart('.').ToLowerInvariant();
            engine = engine switch { "handlebars" => "hbs", "dotliquid" => "liquid", "stringtemplate" => "st", "smartformat" => "smart", _ => engine };
            if (!TemplateEngineFactory.Default.Exists(engine)) throw new ArgumentException("Unknown Didot engine. Use an engine from the generation options catalog.");
            return engine;
        }
        var suffix = Path.GetExtension(template.Identity).ToLowerInvariant();
        return suffix.Length > 0 && TemplateEngineFactory.Default.Exists(suffix) ? suffix.TrimStart('.') : Catalog.DefaultEngine;
    }

    internal static TemplateInput BuiltIn(string target)
    {
        using var stream = typeof(GenerationCatalogService).Assembly.GetManifestResourceStream(
            $"Packata.Application.Generation.Templates.{target}.scriban")
            ?? throw new InvalidOperationException("Bundled generation template is missing.");
        using var reader = new StreamReader(stream);
        return new($"builtin:{target}:v1.scriban", reader.ReadToEnd(), 1);
    }
}
