using System.Text.Json.Nodes;
using System.Text.Json;
using Json.Schema;

namespace Packata.Application.Validation;

public sealed record DescriptorProfile(string Uri, string Label, string Format);

/// <summary>Explicit, offline profile selection. Source URIs never trigger downloads.</summary>
public static class DescriptorProfiles
{
    public static string DataPackageUri => Registrations["datapackage"].Profile.Uri;
    public static string CategoryTablesUri => Registrations["category-tables"].Profile.Uri;
    public static string OdcsUri => Registrations["odcs"].Profile.Uri;
    public static string OdcsSpatialRfc0048Uri => Registrations["odcs-spatial-rfc0048"].Profile.Uri;

    private sealed record Registration(DescriptorProfile Profile, Lazy<JsonSchema> Artifact);
    private sealed record Definition(string Id, string Uri, string Label, string Format, string File, string Dialect);
    private static readonly IReadOnlyDictionary<string, Registration> Registrations = ReadRegistry();

    internal static DescriptorProfile? Select(JsonObject root, bool odcs, bool spatialRfc0048 = false)
    {
        string? uri = DataPackageUri;
        if (odcs) uri = spatialRfc0048 ? OdcsSpatialRfc0048Uri : OdcsUri;
        else if (root.ContainsKey("$schema")) uri = DescriptorValidation.Text(root["$schema"]);
        return Registrations.Values.FirstOrDefault(item => item.Profile.Uri == uri)?.Profile;
    }

    internal static JsonSchema Schema(DescriptorProfile profile) => Registrations.Values.Single(item => item.Profile == profile).Artifact.Value;

    private static IReadOnlyDictionary<string, Registration> ReadRegistry()
    {
        var definitions = JsonSerializer.Deserialize<Definition[]>(ReadResource("profiles.json"), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The pinned profile registry is missing.");
        return definitions.ToDictionary(item => item.Id, item => new Registration(new(item.Uri, item.Label, item.Format),
            new(() => JsonSchema.FromText(SchemaText(item), new BuildOptions { Dialect = item.Dialect switch
            {
                "draft7" => Dialect.Draft07,
                "draft2019-09" => Dialect.Draft201909,
                _ => throw new InvalidOperationException("Unsupported pinned schema dialect.")
            } }))));
    }

    private static string SchemaText(Definition definition)
    {
        var text = ReadResource(definition.File);
        if (definition.Id is not ("odcs" or "odcs-spatial-rfc0048")) return text;

        // The pinned ODCS artifact stays verbatim. Only consumers of the shared Tags
        // definition get the proposed extension, including through nested references.
        var schema = JsonNode.Parse(text)?.AsObject() ?? throw new InvalidOperationException("The pinned schema must be an object.");
        RequireNode(schema, "$defs", "Tags")["items"] = JsonNode.Parse("""
            {
              "oneOf": [
                { "type": "string" },
                {
                  "type": "object",
                  "minProperties": 1,
                  "maxProperties": 1,
                  "propertyNames": { "minLength": 1 },
                  "additionalProperties": { "type": ["string", "number", "boolean"] }
                }
              ]
            }
            """);
        if (definition.Id == "odcs-spatial-rfc0048")
        {
            schema["$id"] = definition.Uri;
            AddSpatialProfile(schema);
        }
        return schema.ToJsonString();
    }

    private static void AddSpatialProfile(JsonObject schema)
    {
        var field = RequireNode(schema, "$defs", "SchemaBaseProperty");
        var types = RequireNode(field, "properties", "logicalType", "enum").AsArray();
        types.Add("geometry");
        types.Add("geography");
        var rules = RequireNode(field, "allOf").AsArray();
        var encodingRule = JsonNode.Parse("""
            {
              "if": { "required": ["logicalType"], "properties": { "logicalType": { "enum": ["geometry", "geography"] } } },
              "then": { "properties": { "logicalTypeOptions": {} } }
            }
            """) ?? throw new InvalidOperationException("The spatial rule must be an object.");
        RequireNode(encodingRule, "then", "properties")["logicalTypeOptions"] = JsonNode.Parse(ReadResource("odcs-spatial-rfc0048-options.json"));
        rules.Add(encodingRule);
        rules.Add(JsonNode.Parse("""
            {
              "if": { "required": ["logicalType"], "properties": { "logicalType": { "const": "geometry" } } },
              "then": {
                "required": ["logicalTypeOptions"],
                "properties": { "logicalTypeOptions": {
                  "required": ["crs"], "properties": { "algorithm": false }
                } }
              }
            }
            """));
    }

    private static JsonNode RequireNode(JsonNode node, params string[] path)
    {
        foreach (var segment in path)
            node = node[segment] ?? throw new InvalidOperationException($"The pinned schema is missing '{segment}'.");
        return node;
    }

    private static string ReadResource(string file)
    {
        using var stream = typeof(DescriptorProfiles).Assembly.GetManifestResourceStream($"Packata.Application.Validation.Schemas.{file}")
            ?? throw new InvalidOperationException("The pinned profile resource is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
