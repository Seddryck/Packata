using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Packata.Application.Generation;

/// <summary>Renders the existing Avro schema tree, without interpreting canonical fields.</summary>
internal sealed class AvroIdlRenderer(CancellationToken token)
{
    private readonly StringBuilder declarations = new();

    internal static string Render(JToken schema, CancellationToken token = default)
    {
        var renderer = new AvroIdlRenderer(token);
        var type = renderer.Type(schema);
        return $"schema {type};\n\n{renderer.declarations}";
    }

    private string Type(JToken schema, string? inheritedNamespace = null)
    {
        token.ThrowIfCancellationRequested();
        if (schema is JArray union) return "union { " + string.Join(", ", union.Select(t => Type(t, inheritedNamespace))) + " }";
        if (schema.Type == JTokenType.String)
        {
            var name = schema.Value<string>()!;
            return name is "null" or "boolean" or "int" or "long" or "float" or "double" or "bytes" or "string" ? name : Identifier(name);
        }
        if (schema is not JObject obj) throw Unsupported("schema", schema);
        var kind = obj.Value<string>("type");
        if (kind is "record" or "enum" or "fixed") return Named(obj, kind, inheritedNamespace);
        if (kind == "bytes" && obj.Value<string>("logicalType") == "decimal")
            return Annotations(obj, "type", "logicalType", "precision", "scale")
                + $"decimal({obj["precision"]}, {obj["scale"]})";
        var properties = Annotations(obj, "type", "items", "values");
        return properties + (kind switch
        {
            "array" => "array<" + Type(obj["items"]!, inheritedNamespace) + ">",
            "map" => "map<" + Type(obj["values"]!, inheritedNamespace) + ">",
            "null" or "boolean" or "int" or "long" or "float" or "double" or "bytes" or "string" => kind,
            _ => throw Unsupported("type", obj)
        });
    }

    private string Named(JObject schema, string kind, string? inheritedNamespace)
    {
        var rawName = schema.Value<string>("name")!;
        var separator = rawName.LastIndexOf('.');
        var name = Identifier(separator < 0 ? rawName : rawName[(separator + 1)..]);
        var ns = separator < 0 ? schema.Value<string>("namespace") ?? inheritedNamespace : rawName[..separator];
        var body = new StringBuilder(Annotations(schema, "type", "name", "namespace", "fields", "symbols", "size", "default"));
        if (ns is not null) body.Append("@namespace(").Append(Json(new JValue(ns))).Append(") ");
        switch (kind)
        {
            case "record":
                body.Append("record ").Append(name).Append(" {\n");
                foreach (var field in schema["fields"]!.Cast<JObject>())
                {
                    var type = Type(field["type"]!, ns);
                    body.Append("    ").Append(type).Append(' ')
                        .Append(Annotations(field, "name", "type", "default"))
                        .Append(Identifier(field.Value<string>("name")!));
                    if (field.TryGetValue("default", out var value)) body.Append(" = ").Append(Json(value));
                    body.Append(";\n");
                }
                body.Append("}\n");
                break;
            case "enum":
                body.Append("enum ").Append(name).Append(" { ")
                    .AppendJoin(", ", schema["symbols"]!.Values<string>().Select(s => Identifier(s!))).Append(" }");
                if (schema.TryGetValue("default", out var symbol)) body.Append(" = ").Append(Identifier(symbol.Value<string>()!)).Append(';');
                body.Append('\n');
                break;
            case "fixed":
                body.Append("fixed ").Append(name).Append('(').Append(schema["size"]).Append(");\n");
                break;
        }
        declarations.Append(body).Append('\n');
        return string.IsNullOrEmpty(ns) ? name : Identifier(ns) + "." + name;
    }

    private static string Annotations(JObject schema, params string[] structural)
    {
        var result = new StringBuilder();
        foreach (var property in schema.Properties().Where(p => !structural.Contains(p.Name)).OrderBy(p => p.Name == "doc" ? 0 : 1))
        {
            if (property.Name == "doc")
            {
                var doc = property.Value.Value<string>()!;
                if (doc.Length == 0 || doc != doc.Trim() || doc.Contains("*/") || doc.Contains('\r')
                    || Regex.IsMatch(doc, @"(^|\n)([^\S\n]|\*)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                    throw Unsupported(property.Name, property.Value);
                result.Append("/**").Append(doc).Append("*/ ");
                continue;
            }
            // IDL annotations accept dotted/dashed identifiers; never drop or rewrite a property.
            if (!Regex.IsMatch(property.Name, "^[A-Za-z_][A-Za-z0-9_]*(?:[.-][A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                throw Unsupported(property.Name, property.Value);
            result.Append('@').Append(property.Name).Append('(').Append(Json(property.Value)).Append(") ");
        }
        return result.ToString();
    }

    private static string Identifier(string name)
        => string.Join(".", name.Split('.').Select(part => "`" + part + "`"));
    private static string Json(JToken value) => value.ToString(Formatting.None);
    private static NotSupportedException Unsupported(string property, JToken value)
        => new($"Avro property '{property}' cannot be faithfully represented in Avro 1.12 IDL.");
}
