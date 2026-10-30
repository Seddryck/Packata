using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Packata.Core.Contracts;

namespace Packata.Application.Validation;

/// <summary>Declaration checks only. Never resolves remote references or opens dataset endpoints.</summary>
internal sealed class OdcsSemanticValidation(JsonObject root, List<ValidationFinding> findings)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private const string IdToken = "id";
    private const string SchemaToken = "schema";
    private const string NameToken = "name";
    private const string TypeToken = "type";
    private const string CustomToken = "custom";
    private const string TeamToken = "team";
    private const string UsernameToken = "username";
    private const string SemStructureToken = "SEM_STRUCTURE";
    private const string SemIdToken = "SEM_ID";
    private const string PropertiesToken = "properties";
    private const string LogicalTypeToken = "logicalType";
    private const string IntegerToken = "integer";
    private const string NumberToken = "number";
    private const string DecimalToken = "decimal";
    private const string DateToken = "date";
    private const string TimestampToken = "timestamp";
    private const string TimeToken = "time";
    private const string ArrayToken = "array";
    private const string MapToken = "map";
    private const string VectorToken = "vector";
    private const string SemanticTypeToken = "semanticType";
    private const string LogicalTypeOptionsToken = "logicalTypeOptions";
    private const string DimensionsToken = "dimensions";
    private const string ItemsToken = "items";
    private const string ValueToken = "value";
    private const string SemBoundsToken = "SEM_BOUNDS";
    private const string MultipleOfToken = "multipleOf";
    private const string PrecisionToken = "precision";
    private const string ScaleToken = "scale";
    private const string EnumToken = "enum";
    private const string SemEnumToken = "SEM_ENUM";
    private const string FromToken = "from";
    private const string SemRelationshipToken = "SEM_RELATIONSHIP";
    private const string ToToken = "to";
    private const string SemReferenceToken = "SEM_REFERENCE";
    private const string MetricToken = "metric";
    private const string LibraryToken = "library";
    private const string SemQualityToken = "SEM_QUALITY";
    private const string ImplementationToken = "implementation";
    private static string At(string path, params object[] segments)
        => string.Join('/', new[] { path }.Concat(segments.Select(segment => Convert.ToString(segment, CultureInfo.InvariantCulture))));

    private readonly Dictionary<string, JsonObject> names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonObject> ids = new(StringComparer.Ordinal);

    internal static void Validate(JsonObject root, List<ValidationFinding> findings) => new OdcsSemanticValidation(root, findings).Run();
    private void Error(string code, string path, string message) => findings.Add(new(ValidationLayers.Semantic, code, MappingSeverity.Error, path, message));
    private static string? Text(JsonNode? node) => DescriptorValidation.Text(node);
    private static bool IsTrue(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
    private static decimal? Number(JsonNode? node) => node?.GetValueKind() == JsonValueKind.Number &&
        decimal.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private void Run()
    {
        foreach (var key in new[] { IdToken, "version", "kind" }.Where(key => string.IsNullOrWhiteSpace(Text(root[key]))))
            Error("SEM_REQUIRED", At("$", key), "A non-empty string is required.");
        if (Text(root["kind"]) is { } kind && kind != "DataContract") Error("SEM_KIND", "$/kind", "kind must be DataContract.");
        if (!root.ContainsKey(SchemaToken)) Error("SEM_SCHEMA", "$/schema", "ODCS requires an explicit schema section for semantic validation.");
        var schemas = Objects(root, SchemaToken, "$", NameToken);
        foreach (var (schema, path) in schemas)
        {
            var name = Text(schema[NameToken]);
            var id = Text(schema[IdToken]);
            if (name is not null) names.TryAdd(name, schema);
            if (id is not null) ids.TryAdd(id, schema);
            Properties(schema, path);
        }
        // Build all scopes first so forward references work.
        foreach (var (schema, path) in schemas) References(schema, path, null);
        Servers();
        Governance();
        Metadata(root, "$");
        Objects(root, "slaProperties", "$");
    }

    private void Servers()
    {
        var servers = Objects(root, "servers", "$", "server");
        foreach (var (server, path) in servers)
        {
            if (string.IsNullOrWhiteSpace(Text(server[TypeToken]))) Error("SEM_SERVER", At(path, TypeToken), "Server type is required.");
            if (server.ContainsKey("encoding") && !EncodingServers.Contains(Text(server[TypeToken])))
                Error("SEM_SERVER", At(path, "encoding"), "This server type does not permit encoding.");
        }
    }

    private static readonly HashSet<string?> EncodingServers = ["azure", "glue", CustomToken, "kafka", "kinesis", "local", "s3", "sftp"];

    private void Governance()
    {
        Objects(root, "roles", "$", "role");
        List<(JsonObject Node, string Path)> members = root[TeamToken] switch
        {
            JsonArray => Objects(root, TeamToken, "$", UsernameToken),
            JsonObject team => Objects(team, "members", "$/team", UsernameToken),
            _ => []
        };
        if (root.ContainsKey(TeamToken) && root[TeamToken] is not (JsonObject or JsonArray)) Error(SemStructureToken, "$/team", "Team must be an object or member array.");
        var usernames = members.Select(item => Text(item.Node[UsernameToken])).ToHashSet(StringComparer.Ordinal);
        foreach (var (member, path) in members)
        {
            if (ReversedDates(member))
                Error("SEM_GOVERNANCE", path, "Team dateIn must not follow dateOut.");
            if (Text(member["replacedByUsername"]) is { } replacement && (!usernames.Contains(replacement) || replacement == Text(member[UsernameToken])))
                Error("SEM_GOVERNANCE", At(path, "replacedByUsername"), "Replacement must identify a different declared team member.");
        }
    }

    private static bool ReversedDates(JsonObject member)
        => DateOnly.TryParse(Text(member["dateIn"]), CultureInfo.InvariantCulture, out var start)
            && DateOnly.TryParse(Text(member["dateOut"]), CultureInfo.InvariantCulture, out var end) && start > end;

    private List<(JsonObject Node, string Path)> Objects(JsonObject owner, string key, string path, string? uniqueName = null)
    {
        var result = new List<(JsonObject, string)>();
        if (!owner.ContainsKey(key)) return result;
        path = At(path, key);
        if (owner[key] is not JsonArray array) { Error(SemStructureToken, path, "An array of declarations is required."); return result; }
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var localNames = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < array.Count; index++)
        {
            var itemPath = At(path, index);
            if (array[index] is not JsonObject item) { Error(SemStructureToken, itemPath, "A declaration object is required."); continue; }
            result.Add((item, itemPath));
            ScopedId(item, itemPath, identifiers);
            if (uniqueName is not null) ScopedName(item, itemPath, uniqueName, localNames);
        }
        return result;
    }

    private static bool ValidId([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? id) => id is not null && Regex.IsMatch(id, @"^[^\s.#/\\@!%&^]+$", RegexOptions.None, Timeout);

    private void ScopedId(JsonObject item, string path, HashSet<string> identifiers)
    {
        if (!item.ContainsKey(IdToken)) return;
        var id = Text(item[IdToken]);
        if (!ValidId(id)) Error(SemIdToken, At(path, IdToken), "Stable IDs must be non-empty strings without reserved characters or whitespace.");
        else if (!identifiers.Add(id)) Error(SemIdToken, At(path, IdToken), "Stable IDs must be unique within their containing array.");
    }

    private void ScopedName(JsonObject item, string path, string key, HashSet<string> localNames)
    {
        var name = Text(item[key]);
        if (string.IsNullOrWhiteSpace(name)) Error("SEM_NAME", At(path, key), "A non-empty name is required.");
        else if (!localNames.Add(name)) Error("SEM_NAME", At(path, key), "Names must be unique in this scope.");
    }

    private void Properties(JsonObject owner, string path)
    {
        var properties = Objects(owner, PropertiesToken, path, NameToken);
        Positions(properties, "primaryKey", "primaryKeyPosition");
        Positions(properties, "partitioned", "partitionKeyPosition");
        foreach (var (property, propertyPath) in properties) Property(property, propertyPath);
        Quality(owner, path);
    }

    private void Property(JsonObject property, string path)
    {
        var type = Text(property[LogicalTypeToken]);
        PropertyType(property, path, type);
        Enumeration(property, path, type);
        Properties(property, path);
        Containers(property, path, type);
    }

    private static readonly HashSet<string?> LogicalTypes = ["string", IntegerToken, NumberToken, DecimalToken, DateToken, TimestampToken, TimeToken, "boolean", "object", ArrayToken, MapToken, VectorToken];
    private static bool PositiveInteger(JsonNode? node) => Number(node) is { } n && n > 0 && decimal.Truncate(n) == n;

    private void PropertyType(JsonObject property, string path, string? type)
    {
        if (property.ContainsKey(LogicalTypeToken) && !LogicalTypes.Contains(type))
            Error("SEM_TYPE", At(path, LogicalTypeToken), "Unsupported logical type.");
        if (property.ContainsKey(SemanticTypeToken) && Text(property[SemanticTypeToken]) is not ("column" or "measure" or "dimension"))
            Error("SEM_TYPE", At(path, SemanticTypeToken), "Expected column, measure or dimension.");
        PropertyOptions(property, path, type);
    }

    private void PropertyOptions(JsonObject property, string path, string? type)
    {
        if (property.ContainsKey(LogicalTypeOptionsToken) && property[LogicalTypeOptionsToken] is not JsonObject)
            Error("SEM_OPTIONS", At(path, LogicalTypeOptionsToken), "Logical type options must be an object.");
        var options = property[LogicalTypeOptionsToken] as JsonObject;
        if (type == VectorToken && !PositiveInteger(options?[DimensionsToken]))
            Error("SEM_VECTOR", At(path, LogicalTypeOptionsToken, DimensionsToken), "A vector requires a positive integer dimensions option.");
        if (options is not null) Options(options, type, At(path, LogicalTypeOptionsToken));
    }

    private void Containers(JsonObject property, string path, string? type)
    {
        if (type == ArrayToken && property[ItemsToken] is not JsonObject) Error("SEM_ARRAY", At(path, ItemsToken), "Array items must declare an element object.");
        if (property.ContainsKey(ItemsToken))
        {
            if (property[ItemsToken] is JsonObject items) Property(items, At(path, ItemsToken));
            else Error("SEM_ARRAY", At(path, ItemsToken), "Array items must be an object.");
        }
        MapMembers(property, path, type);
    }

    private void MapMembers(JsonObject property, string path, string? type)
    {
        if (type == MapToken && property[MapToken] is not JsonObject) Error("SEM_MAP", At(path, MapToken), "A map requires key and value declarations.");
        if (property[MapToken] is JsonObject map)
            foreach (var key in new[] { "key", ValueToken })
                if (map[key] is JsonObject member) Property(member, At(path, MapToken, key));
                else Error("SEM_MAP", At(path, MapToken, key), "A map member declaration is required.");
    }

    private void Positions(List<(JsonObject Node, string Path)> properties, string flag, string position)
    {
        var positions = new HashSet<decimal>();
        foreach (var (property, itemPath) in properties)
        {
            if (!property.ContainsKey(position)) continue; // Positions are optional; -1 means unspecified.
            var value = Number(property[position]);
            if (value == -1) continue;
            if (!IsTrue(property[flag]) || value is null || value < 1 || decimal.Truncate(value.Value) != value || !positions.Add(value.Value))
                Error("SEM_POSITION", At(itemPath, position), "Positions must be unique positive integers on flagged properties, or -1 when unspecified.");
        }
    }

    private void Options(JsonObject options, string? type, string path)
    {
        if (type is null && options.Count > 0) Error("SEM_OPTIONS", path, "Logical type options require a declared logicalType for meaningful validation.");
        CollectionBounds(options, path);
        ValueBounds(options, type, path);
        DecimalOptions(options, type, path);
    }

    private void CollectionBounds(JsonObject options, string path)
    {
        foreach (var (minimum, maximum) in new[] { ("minLength", "maxLength"), ("minItems", "maxItems"), ("minProperties", "maxProperties") })
        {
            foreach (var key in new[] { minimum, maximum })
                if (options.ContainsKey(key) && (Number(options[key]) is not { } n || n < 0 || decimal.Truncate(n) != n))
                    Error(SemBoundsToken, At(path, key), "A non-negative integer bound is required.");
            if (Number(options[minimum]) is { } min && Number(options[maximum]) is { } max && min > max)
                Error(SemBoundsToken, path, "Minimum bound must not exceed maximum bound.");
        }
    }

    private static readonly HashSet<string?> NumericTypes = [IntegerToken, NumberToken, DecimalToken];
    private static readonly HashSet<string?> TemporalTypes = [DateToken, TimeToken, TimestampToken];
    private static bool ValidBound(JsonNode? bound, string? type)
    {
        if (bound is null) return false;
        if (NumericTypes.Contains(type)) return Number(bound) is not null;
        return TemporalTypes.Contains(type) && Compatible(bound, type);
    }

    private void ValueBounds(JsonObject options, string? type, string path)
    {
        if (options.ContainsKey(MultipleOfToken) && (Number(options[MultipleOfToken]) is not > 0)) Error(SemBoundsToken, At(path, MultipleOfToken), "multipleOf must be positive.");
        var lows = new[] { "minimum", "exclusiveMinimum" }.Where(options.ContainsKey).ToArray();
        var highs = new[] { "maximum", "exclusiveMaximum" }.Where(options.ContainsKey).ToArray();
        foreach (var key in lows.Concat(highs).Where(key => !ValidBound(options[key], type)))
            Error(SemBoundsToken, At(path, key), "Bound must match the numeric or temporal logical type without coercion.");
        foreach (var low in lows)
            foreach (var high in highs)
            {
                Interval(options, low, high, type, path);
            }
    }

    private void Interval(JsonObject options, string low, string high, string? type, string path)
    {
        var comparison = Compare(options[low], options[high], type);
        if (comparison is null) Error(SemBoundsToken, path, "Bounds must have comparable values for the declared logical type.");
        else if (comparison > 0 || (comparison == 0 && (low == "exclusiveMinimum" || high == "exclusiveMaximum")))
            Error(SemBoundsToken, path, "Bounds describe an empty interval.");
    }

    private void DecimalOptions(JsonObject options, string? type, string path)
    {
        foreach (var key in new[] { PrecisionToken, ScaleToken }.Where(key => options.ContainsKey(key) && !ValidDecimalParameter(type, key, options[key])))
            Error("SEM_DECIMAL", At(path, key), "Decimal precision must be positive and scale non-negative integers on a number type.");
        if (Number(options[ScaleToken]) is { } scale && (Number(options[PrecisionToken]) is not { } precision || scale > precision))
            Error("SEM_DECIMAL", path, "Scale requires precision and must not exceed it.");
    }

    private static bool ValidDecimalParameter(string? type, string key, JsonNode? node)
    {
        if (type is not (NumberToken or DecimalToken)) return false;
        return Number(node) is { } n && decimal.Truncate(n) == n && n >= (key == PrecisionToken ? 1 : 0);
    }

    private static int? Compare(JsonNode? first, JsonNode? second, string? type)
    {
        if (Number(first) is { } a && Number(second) is { } b) return a.CompareTo(b);
        if (type == DateToken && DateOnly.TryParse(Text(first), CultureInfo.InvariantCulture, out var da) && DateOnly.TryParse(Text(second), CultureInfo.InvariantCulture, out var db)) return da.CompareTo(db);
        if (type == TimeToken && TimeOnly.TryParse(Text(first), CultureInfo.InvariantCulture, out var ta) && TimeOnly.TryParse(Text(second), CultureInfo.InvariantCulture, out var tb)) return ta.CompareTo(tb);
        if (type == TimestampToken && DateTimeOffset.TryParse(Text(first), CultureInfo.InvariantCulture, DateTimeStyles.None, out var sa) && DateTimeOffset.TryParse(Text(second), CultureInfo.InvariantCulture, DateTimeStyles.None, out var sb)) return sa.CompareTo(sb);
        return null;
    }

    private void Enumeration(JsonObject property, string path, string? type)
    {
        var entries = Objects(property, EnumToken, path);
        if (property.ContainsKey(EnumToken) && entries.Count == 0) Error(SemEnumToken, At(path, EnumToken), "An enumeration requires at least one value.");
        var values = new List<JsonNode?>();
        foreach (var (entry, entryPath) in entries)
        {
            Metadata(entry, entryPath);
            var value = entry[ValueToken];
            if (!entry.ContainsKey(ValueToken) || value is null || value is JsonArray or JsonObject)
                Error(SemEnumToken, At(entryPath, ValueToken), "Enum values must be non-null scalar values.");
            else if (!Compatible(value, type)) Error(SemEnumToken, At(entryPath, ValueToken), "Enum value type must match the declared logical type without coercion.");
            if (values.Any(existing => JsonNode.DeepEquals(existing, value))) Error(SemEnumToken, At(entryPath, ValueToken), "Enum values must be unique, independently of labels or metadata.");
            values.Add(value);
        }
    }

    private static bool Compatible(JsonNode value, string? type) => type switch
    {
        "string" => value.GetValueKind() == JsonValueKind.String,
        IntegerToken => Number(value) is { } number && decimal.Truncate(number) == number,
        NumberToken or DecimalToken => value.GetValueKind() == JsonValueKind.Number,
        "boolean" => value.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        DateToken => DateOnly.TryParse(Text(value), CultureInfo.InvariantCulture, out _),
        TimeToken => TimeOnly.TryParse(Text(value), CultureInfo.InvariantCulture, out _),
        TimestampToken => DateTimeOffset.TryParse(Text(value), CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        ArrayToken or MapToken or "object" or VectorToken => false,
        _ => true
    };

    private void References(JsonObject owner, string path, JsonObject? property)
    {
        foreach (var (relationship, relationPath) in Objects(owner, "relationships", path))
            Relationship(relationship, relationPath, property);
        foreach (var (child, childPath) in Objects(owner, PropertiesToken, path)) References(child, childPath, child);
        if (owner[ItemsToken] is JsonObject items) References(items, At(path, ItemsToken), items);
        if (owner[MapToken] is JsonObject map)
            foreach (var key in new[] { "key", ValueToken }) if (map[key] is JsonObject member) References(member, At(path, MapToken, key), member);
    }

    private void Relationship(JsonObject relationship, string relationPath, JsonObject? property)
    {
        if (property is not null && relationship.ContainsKey(FromToken)) Error(SemRelationshipToken, At(relationPath, FromToken), "Property-level relationship sources are implicit.");
        var from = property is not null ? new List<JsonObject?> { property } : ResolveMany(relationship[FromToken], At(relationPath, FromToken));
        var to = ResolveMany(relationship[ToToken], At(relationPath, ToToken));
        if (property is null && (relationship[FromToken] is JsonArray) != (relationship[ToToken] is JsonArray))
            Error(SemRelationshipToken, relationPath, "Relationship sides must have matching scalar or array shapes.");
        if (from.Count != to.Count) Error(SemRelationshipToken, relationPath, "Composite relationship sides must have equal lengths.");
        for (var index = 0; index < Math.Min(from.Count, to.Count); index++)
        {
            ReferenceTypes(from[index], to[index], relationPath);
        }
    }

    private void ReferenceTypes(JsonObject? from, JsonObject? to, string path)
    {
        var left = Text(from?[LogicalTypeToken]);
        var right = Text(to?[LogicalTypeToken]);
        if (left is not null && right is not null && left != right) Error("SEM_REFERENCE_TYPE", path, "Referenced properties must have matching declared logical types.");
        if (from?[LogicalTypeOptionsToken] is not JsonObject leftOptions || to?[LogicalTypeOptionsToken] is not JsonObject rightOptions) return;
        foreach (var option in new[] { PrecisionToken, ScaleToken, DimensionsToken }.Where(option => ConflictingOptions(leftOptions, rightOptions, option)))
            Error("SEM_REFERENCE_TYPE", path, "Referenced properties must have compatible declared type options.");
    }

    private static bool ConflictingOptions(JsonObject left, JsonObject right, string key)
        => left.ContainsKey(key) && right.ContainsKey(key) && !JsonNode.DeepEquals(left[key], right[key]);

    private List<JsonObject?> ResolveMany(JsonNode? node, string path)
    {
        var refs = node is JsonArray array ? array.ToArray() : new[] { node };
        if (refs.Length == 0) Error(SemReferenceToken, path, "At least one property reference is required.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return refs.Select(reference =>
        {
            var text = Text(reference);
            if (text is null || !seen.Add(text)) { Error(SemReferenceToken, path, "References must be non-empty, unique strings."); return null; }
            if (text.Contains('#')) { Error("SEM_EXTERNAL_REFERENCE", path, "External references cannot be resolved by offline validation; semantic validation is incomplete."); return null; }
            return ResolvePath(text, path);
        }).ToList();
    }

    private JsonObject? ResolvePath(string text, string path)
    {
        var qualified = text.Contains('/');
        var parts = text.TrimStart('/').Split(qualified ? '/' : '.');
        JsonObject? current = null;
        if (qualified && parts.Length >= 2 && parts[0] == SchemaToken) ids.TryGetValue(parts[1], out current);
        else if (!qualified && parts.Length >= 2) names.TryGetValue(parts[0], out current);
        current = DescendReference(current, qualified, parts);
        if (current is null || (qualified && parts.Length < 4)) Error(SemReferenceToken, path, "Reference must identify an existing property in this contract.");
        return current;
    }

    private static JsonObject? DescendReference(JsonObject? current, bool qualified, string[] parts)
    {
        for (var index = qualified ? 2 : 1; current is not null && index < parts.Length; index += qualified ? 2 : 1)
        {
            if (qualified && (index + 1 >= parts.Length || parts[index] != PropertiesToken)) { current = null; break; }
            var target = parts[qualified ? index + 1 : index];
            current = (current[PropertiesToken] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(p => Text(p[qualified ? IdToken : NameToken]) == target);
        }
        return current;
    }

    private void Quality(JsonObject owner, string path)
    {
        foreach (var (rule, rulePath) in Objects(owner, "quality", path))
        {
            var type = Text(rule[TypeToken]) ?? (rule.ContainsKey(MetricToken) ? LibraryToken : "text");
            QualityDefinition(rule, rulePath, type);
            QualityExpectations(rule, rulePath, type);
        }
    }

    private void QualityDefinition(JsonObject rule, string rulePath, string type)
    {
        var required = type switch { LibraryToken => MetricToken, "sql" => "query", CustomToken => "engine", "text" => "description", _ => null };
        if (required is null || string.IsNullOrWhiteSpace(Text(rule[required]))) Error(SemQualityToken, rulePath, "Quality declaration requires its type-specific definition.");
        if (type == CustomToken && rule[ImplementationToken] is null) Error(SemQualityToken, At(rulePath, ImplementationToken), "Custom quality requires an implementation declaration.");
        if (type == LibraryToken && !LibraryMetrics.Contains(Text(rule[MetricToken])))
            Error(SemQualityToken, At(rulePath, MetricToken), "Unknown ODCS library metric.");
        if (rule.ContainsKey(MetricToken) && rule.ContainsKey("rule") && Text(rule[MetricToken]) != Text(rule["rule"])) Error(SemQualityToken, rulePath, "metric conflicts with the deprecated rule declaration.");
    }

    private static readonly HashSet<string?> LibraryMetrics = ["nullValues", "missingValues", "invalidValues", "duplicateValues", "rowCount"];

    private void QualityExpectations(JsonObject rule, string rulePath, string type)
    {
        foreach (var key in new[] { "mustBeBetween", "mustNotBeBetween" })
            if (rule.ContainsKey(key) && (rule[key] is not JsonArray { Count: 2 } range || Compare(range[0], range[1], null) is not < 0))
                Error(SemQualityToken, At(rulePath, key), "Range expectations require two ascending numeric bounds.");
        OrderingExpectations(rule, rulePath);
        if (type is LibraryToken or "sql" && !rule.Any(p => p.Key.StartsWith("must", StringComparison.Ordinal))) Error(SemQualityToken, rulePath, "A measured quality declaration requires an expectation.");
    }

    private void OrderingExpectations(JsonObject rule, string rulePath)
    {
        foreach (var key in new[] { "mustBeGreaterThan", "mustBeGreaterOrEqualTo", "mustBeLessThan", "mustBeLessOrEqualTo" }
            .Where(key => rule.ContainsKey(key) && Number(rule[key]) is null))
            Error(SemQualityToken, At(rulePath, key), "Ordering expectations require a numeric operand.");
    }

    private void Metadata(JsonObject owner, string path)
    {
        if ((path is "$/team" or "$/price") && owner.ContainsKey(IdToken) && !ValidId(Text(owner[IdToken])))
            Error(SemIdToken, At(path, IdToken), "Stable IDs must be non-empty strings without reserved characters or whitespace.");
        // Only traverse descriptor declaration scopes; vendor payloads and examples are opaque.
        foreach (var key in new[] { "authoritativeDefinitions", "synonyms", "support", "customProperties", "verifiedStatements", "constraints" }) Objects(owner, key, path);
        foreach (var pair in owner)
        {
            if (OpaqueMetadata.Contains(pair.Key)) continue;
            NestedMetadata(pair.Value, At(path, pair.Key));
        }
    }

    private static readonly HashSet<string> OpaqueMetadata = ["customProperties", LogicalTypeOptionsToken, "examples", "arguments", ImplementationToken, EnumToken, ValueToken];

    private void NestedMetadata(JsonNode? node, string path)
    {
        if (node is JsonObject child) Metadata(child, path);
        else if (node is JsonArray array)
            for (var index = 0; index < array.Count; index++)
                if (array[index] is JsonObject item) Metadata(item, At(path, index));
    }
}
