using System.Globalization;

namespace Packata.Core.Contracts;

// A deliberately conservative source-member allowlist, separate from factual differencing.
// Unknown members must remain visible even when a native deserializer ignores them.
internal static class CompatibilityCoverage
{
    internal static IEnumerable<string> Unsupported(IReadOnlyDictionary<string, object?> descriptor, ContractStandard standard)
    {
        var paths = new List<string>();
        Visit(descriptor, standard == ContractStandard.Odcs ? "odcs" : "package", "$", null);
        return paths.Order(StringComparer.Ordinal);

        void Visit(object? value, string context, string path, string? logicalType)
        {
            if (value is not IReadOnlyDictionary<string, object?> map) { paths.Add(path); return; }
            if (context == "odcs-field") logicalType = map.GetValueOrDefault("logicalType")?.ToString();
            foreach (var (key, child) in map)
            {
                var childPath = path + "." + key;
                var rule = Rule(context, key, logicalType);
                if (rule is null) { paths.Add(childPath); continue; }
                if (rule == "leaf")
                {
                    // Scalars or scalar arrays only; an unexpected object could be silently dropped.
                    if (child is IReadOnlyDictionary<string, object?> || child is IEnumerable<object?> list && list.Any(x => x is IReadOnlyDictionary<string, object?>))
                        paths.Add(childPath);
                    else if (!SupportedValue(context, key, child, logicalType)) paths.Add(childPath);
                    continue;
                }
                if (rule.EndsWith("[]", StringComparison.Ordinal))
                {
                    if (child is not IEnumerable<object?> items) { paths.Add(childPath); continue; }
                    var index = 0;
                    foreach (var item in items) Visit(item, rule[..^2], $"{childPath}[{index++}]", logicalType);
                }
                else Visit(child, rule, childPath, logicalType);
            }
        }
    }

    private static bool SupportedValue(string context, string key, object? value, string? type)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (context is "odcs-field" or "dp-constraints" && key is "required" or "nullable" or "unique" or "primaryKey")
            return bool.TryParse(text, out _);
        if (context is not ("odcs-options" or "dp-constraints")) return true;
        if (key is "uniqueItems" || context == "odcs-options" && key is "exclusiveMinimum" or "exclusiveMaximum")
            return bool.TryParse(text, out _);
        if (key is "minLength" or "maxLength" or "minItems" or "maxItems" or "minProperties" or "maxProperties")
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count >= 0;
        if (key is "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum")
        {
            if (context == "odcs-options" && type == "integer")
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            // ODCS decimal mapping currently uses the process culture. Refuse ambiguous
            // representations instead of silently trusting a dropped/misread bound.
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant)
                && (context != "odcs-options" || decimal.TryParse(text, out var mapped) && mapped == invariant);
        }
        return true;
    }

    private static string? Rule(string context, string key, string? type) => context switch
    {
        "odcs" => key switch
        {
            "apiVersion" or "kind" or "id" or "name" or "version" or "status" or "domain" or "tags" => "leaf",
            "description" => "odcs-description", "schema" => "odcs-dataset[]", "servers" => "server[]",
            _ => null
        },
        "odcs-description" => key is "purpose" or "usage" or "limitations" ? "leaf" : null,
        "odcs-dataset" => key switch
        {
            "name" or "physicalName" or "physicalType" or "description" => "leaf",
            "properties" => "odcs-field[]", _ => null
        },
        "odcs-field" => key switch
        {
            "name" or "logicalType" or "physicalName" or "physicalType" or "description" or "required" or "nullable"
                or "unique" or "primaryKey" or "primaryKeyPosition" => "leaf",
            "properties" => "odcs-field[]", "items" => "odcs-field", "logicalTypeOptions" => "odcs-options",
            "enum" => "odcs-enum[]", _ => null
        },
        "odcs-enum" => key == "value" ? "leaf" : null,
        "odcs-options" => (type, key) switch
        {
            ("string", "format" or "minLength" or "maxLength" or "pattern") => "leaf",
            ("integer" or "number" or "decimal", "format" or "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum") => "leaf",
            ("array", "minItems" or "maxItems" or "uniqueItems") => "leaf",
            ("object", "minProperties" or "maxProperties" or "required") => "leaf",
            // Temporal bound implications and additional logical types remain unsupported.
            ("date" or "time" or "timestamp" or "datetime", "format") => "leaf",
            _ => null
        },
        "server" => key is "server" or "type" or "description" or "environment" or "path" or "location" or "host"
            or "port" or "database" or "schema" or "catalog" or "format" or "encoding" ? "leaf" : null,
        "package" => key switch
        {
            "name" or "id" or "title" or "description" or "version" or "homepage" or "profile" or "created" or "keywords" => "leaf",
            "resources" => "resource[]", _ => null
        },
        "resource" => key switch
        {
            "name" or "title" or "description" or "path" or "format" or "mediatype" or "encoding" or "compression"
                or "type" or "kind" or "profile" or "homepage" or "bytes" or "hash" => "leaf",
            "schema" => "dp-schema", "dialect" => "dialect", _ => null
        },
        "dp-schema" => key switch
        {
            "fields" => "dp-field[]", "primaryKey" => "leaf", _ => null
        },
        "dp-field" => key switch
        {
            "name" or "type" or "format" or "title" or "description" or "example" or "rdfType"
                or "x-packata-physicalName" or "x-packata-physicalType" => "leaf",
            "constraints" => "dp-constraints", _ => null
        },
        "dp-constraints" => key is "required" or "unique" or "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum"
            or "minLength" or "maxLength" or "pattern" or "enum" ? "leaf" : null,
        "dialect" => key is "header" or "headerRows" or "headerJoin" or "commentRows" or "commentChar" or "delimiter"
            or "lineTerminator" or "quoteChar" or "doubleQuote" or "escapeChar" or "skipInitialSpace" ? "leaf" : null,
        _ => null
    };
}
