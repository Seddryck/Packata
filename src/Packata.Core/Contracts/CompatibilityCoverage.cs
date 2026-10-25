using System.Globalization;

namespace Packata.Core.Contracts;

// A deliberately conservative source-member allowlist, separate from factual differencing.
// Unknown members must remain visible even when a native deserializer ignores them.
internal static class CompatibilityCoverage
{
    private const string OdcsField = "odcs-field";
    private const string OdcsOptions = "odcs-options";
    private const string DataPackageConstraints = "dp-constraints";
    private const string Required = "required";
    private const string ExclusiveMinimum = "exclusiveMinimum";
    private const string ExclusiveMaximum = "exclusiveMaximum";
    private const string Description = "description";
    private const string Format = "format";

    internal static IEnumerable<string> Unsupported(IReadOnlyDictionary<string, object?> descriptor, ContractStandard standard)
    {
        var visitor = new DescriptorVisitor();
        visitor.Visit(descriptor, standard == ContractStandard.Odcs ? "odcs" : "package", "$", null);
        return visitor.Paths.Order(StringComparer.Ordinal);
    }

    private sealed class DescriptorVisitor
    {
        internal List<string> Paths { get; } = [];

        internal void Visit(object? value, string context, string path, string? logicalType)
        {
            if (value is not IReadOnlyDictionary<string, object?> map) { Paths.Add(path); return; }
            if (context == OdcsField) logicalType = map.GetValueOrDefault("logicalType")?.ToString();
            foreach (var (key, child) in map)
                VisitMember(context, key, child, path + "." + key, logicalType);
        }

        private void VisitMember(string context, string key, object? child, string path, string? logicalType)
        {
            var rule = Rule(context, key, logicalType);
            if (rule is null) { Paths.Add(path); return; }
            if (rule == "leaf")
            {
                if (!IsScalarOrScalarArray(child) || !SupportedValue(context, key, child, logicalType)) Paths.Add(path);
                return;
            }
            if (!rule.EndsWith("[]", StringComparison.Ordinal))
            {
                Visit(child, rule, path, logicalType);
                return;
            }
            VisitArray(child, rule[..^2], path, logicalType);
        }

        private void VisitArray(object? child, string context, string path, string? logicalType)
        {
            if (child is not IEnumerable<object?> items) { Paths.Add(path); return; }
            var index = 0;
            foreach (var item in items) Visit(item, context, $"{path}[{index++}]", logicalType);
        }
        // An unexpected object in a scalar position could be silently dropped by a native parser.
        private static bool IsScalarOrScalarArray(object? value) => value is not IReadOnlyDictionary<string, object?>
            && (value is not IEnumerable<object?> list || !list.Any(x => x is IReadOnlyDictionary<string, object?>));

        private static bool SupportedValue(string context, string key, object? value, string? type)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (context is OdcsField or DataPackageConstraints && key is Required or "nullable" or "unique" or "primaryKey")
                return bool.TryParse(text, out _);
            if (context is not (OdcsOptions or DataPackageConstraints)) return true;
            if (key is "uniqueItems" || context == OdcsOptions && key is ExclusiveMinimum or ExclusiveMaximum)
                return bool.TryParse(text, out _);
            if (key is "minLength" or "maxLength" or "minItems" or "maxItems" or "minProperties" or "maxProperties")
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count >= 0;
            if (key is "minimum" or "maximum" or ExclusiveMinimum or ExclusiveMaximum)
                return SupportedBound(context, text, type);
            return true;
        }

        private static bool SupportedBound(string context, string? text, string? type)
        {
            if (context == OdcsOptions && type == "integer")
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            // ODCS decimal mapping currently uses the process culture. Refuse ambiguous
            // representations instead of silently trusting a dropped/misread bound.
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant)
                && (context != OdcsOptions || decimal.TryParse(text, out var mapped) && mapped == invariant);
        }

        private static string? Rule(string context, string key, string? type) => context switch
        {
            "odcs" => OdcsRule(key),
            "odcs-description" => key is "purpose" or "usage" or "limitations" ? "leaf" : null,
            "odcs-dataset" => OdcsDatasetRule(key),
            OdcsField => OdcsFieldRule(key),
            "odcs-enum" => key == "value" ? "leaf" : null,
            OdcsOptions => OdcsOptionsRule(type, key),
            "server" => ServerRule(key),
            "package" => PackageRule(key),
            "resource" => ResourceRule(key),
            "dp-schema" => DataPackageSchemaRule(key),
            "dp-field" => DataPackageFieldRule(key),
            DataPackageConstraints => DataPackageConstraintRule(key),
            "dialect" => DialectRule(key),
            _ => null
        };

        private static string? OdcsRule(string key) => key switch
        {
            "apiVersion" or "kind" or "id" or "name" or "version" or "status" or "domain" or "tags" => "leaf",
            Description => "odcs-description",
            "schema" => "odcs-dataset[]",
            "servers" => "server[]",
            _ => null
        };

        private static string? OdcsDatasetRule(string key) => key switch
        {
            "name" or "physicalName" or "physicalType" or Description => "leaf",
            "properties" => "odcs-field[]",
            _ => null
        };

        private static string? OdcsFieldRule(string key) => key switch
        {
            "name" or "logicalType" or "physicalName" or "physicalType" or Description or Required or "nullable"
                or "unique" or "primaryKey" or "primaryKeyPosition" => "leaf",
            "properties" => "odcs-field[]",
            "items" => OdcsField,
            "logicalTypeOptions" => OdcsOptions,
            "enum" => "odcs-enum[]",
            _ => null
        };

        private static string? OdcsOptionsRule(string? type, string key) => (type, key) switch
        {
            ("string", Format or "minLength" or "maxLength" or "pattern") => "leaf",
            ("integer" or "number" or "decimal", Format or "minimum" or "maximum" or ExclusiveMinimum or ExclusiveMaximum) => "leaf",
            ("array", "minItems" or "maxItems" or "uniqueItems") => "leaf",
            ("object", "minProperties" or "maxProperties" or Required) => "leaf",
            // Temporal bound implications and additional logical types remain unsupported.
            ("date" or "time" or "timestamp" or "datetime", Format) => "leaf",
            _ => null
        };

        private static string? ServerRule(string key) => key is "server" or "type" or Description or "environment" or "path" or "location" or "host"
            or "port" or "database" or "schema" or "catalog" or Format or "encoding" ? "leaf" : null;

        private static string? PackageRule(string key) => key switch
        {
            "name" or "id" or "title" or Description or "version" or "homepage" or "profile" or "created" or "keywords" => "leaf",
            "resources" => "resource[]",
            _ => null
        };

        private static string? ResourceRule(string key) => key switch
        {
            "name" or "title" or Description or "path" or Format or "mediatype" or "encoding" or "compression"
                or "type" or "kind" or "profile" or "homepage" or "bytes" or "hash" => "leaf",
            "schema" => "dp-schema",
            "dialect" => "dialect",
            _ => null
        };

        private static string? DataPackageSchemaRule(string key) => key switch
        {
            "fields" => "dp-field[]",
            "primaryKey" => "leaf",
            _ => null
        };

        private static string? DataPackageFieldRule(string key) => key switch
        {
            "name" or "type" or Format or "title" or Description or "example" or "rdfType"
                or "x-packata-physicalName" or "x-packata-physicalType" => "leaf",
            "constraints" => DataPackageConstraints,
            _ => null
        };

        private static string? DataPackageConstraintRule(string key) => key is Required or "unique" or "minimum" or "maximum" or ExclusiveMinimum or ExclusiveMaximum
            or "minLength" or "maxLength" or "pattern" or "enum" ? "leaf" : null;

        private static string? DialectRule(string key) => key is "header" or "headerRows" or "headerJoin" or "commentRows" or "commentChar" or "delimiter"
            or "lineTerminator" or "quoteChar" or "doubleQuote" or "escapeChar" or "skipInitialSpace" ? "leaf" : null;
    }
}
