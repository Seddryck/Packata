using System.Data;
using System.Data.Common;
using System.Text;
using DubUrl;
using DubUrl.Discovery;
using DubUrl.Schema;
using DubUrl.Schema.Constraints;
using Packata.Database;
using Packata.Core.Contracts;
using DatabaseForeignKeyConstraint = DubUrl.Schema.Constraints.ForeignKeyConstraint;
using DatabaseIndex = DubUrl.Schema.Index;
using DatabaseSchema = DubUrl.Schema.Schema;

namespace Packata.ResourceReaders.Database;

/// <summary>Infers a standards-neutral canonical contract from an existing database schema.</summary>
public sealed class DatabaseDataContractInference
{
    private const string DatabaseLiteral = "database";
    private const string ExtensionNamespace = "duburl";
    private const string RedactedQueryValue = "%5BREDACTED%5D";
    private const string StringLogicalType = "string";
    private readonly DatabaseDataContractInferenceOptions _options;
    private readonly SchemaInspector _inspector;

    public DatabaseDataContractInference(DatabaseDataContractInferenceOptions? options = null)
        : this(options, new SchemaInspector())
    { }

    internal DatabaseDataContractInference(DatabaseDataContractInferenceOptions? options, SchemaInspector inspector)
    {
        _options = options ?? new DatabaseDataContractInferenceOptions();
        ArgumentNullException.ThrowIfNull(_options.DiscoveryOptions);
        ArgumentNullException.ThrowIfNull(_options.Assets);
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    /// <summary>Inspects a database URL and omits its credentials from the inferred contract.</summary>
    public Task<DataContractInferenceResult> InferAsync(string connectionUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionUrl);
        return InferAsync(new ConnectionUrl(connectionUrl), cancellationToken);
    }

    /// <summary>Inspects a DubUrl connection and omits its credentials from the inferred contract.</summary>
    public async Task<DataContractInferenceResult> InferAsync(ConnectionUrl connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var location = Sanitize(connection.Url);
        var inspection = await _inspector.InspectAsync(
            connection, _options.DiscoveryOptions, cancellationToken).ConfigureAwait(false);
        return Map(inspection, location, SafeSource(location));
    }

    /// <summary>
    /// Inspects a canonical database location. Relative SQLite and DuckDB paths are resolved from
    /// <paramref name="rootPath"/> when supplied.
    /// </summary>
    public async Task<DataContractInferenceResult> InferAsync(ConnectionLocation location, string? rootPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        using var connections = new DubUrlConnectionService(rootPath ?? string.Empty);
        return await InferAsync(connections.Resolve(location), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inspects an open borrowed connection. The caller retains ownership of the connection.
    /// </summary>
    public async Task<DataContractInferenceResult> InferAsync(DbConnection connection,
        ISchemaMetadataReader reader, ConnectionLocation endpointLocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(endpointLocation);
        var location = Sanitize(endpointLocation);
        var inspection = await _inspector.InspectAsync(
            connection, reader, _options.DiscoveryOptions, cancellationToken).ConfigureAwait(false);
        return Map(inspection, location, SafeSource(location));
    }

    internal DataContractInferenceResult Map(SchemaInspectionResult inspection,
        ConnectionLocation location, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var diagnostics = MapDiagnostics(inspection, sourcePath);
        if (inspection.Schema is null)
            return new DataContractInferenceResult(null, diagnostics);

        var tables = inspection.Schema.Tables.Values.ToArray();
        var views = _options.DiscoveryOptions.Features.Contains(SchemaFeature.Views)
            ? inspection.ViewInventory.ToArray()
            : [];
        var identities = tables.Select(x => x.Identity)
            .Concat(views.Select(x => Name(x.Identity)))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToArray();
        var assetIds = BuildAssetIds(identities);
        var relations = identities.ToDictionary(x => x.Key);
        var endpoints = BuildEndpoints(identities, location);
        var assets = new List<DataAsset>(identities.Length);

        foreach (var table in tables)
            assets.Add(MapTable(table, inspection.Schema, assetIds, relations, endpoints, diagnostics, sourcePath));
        foreach (var view in views)
            assets.Add(MapView(view, assetIds, endpoints, diagnostics, sourcePath));

        var sourceName = location.Database ?? location.Host ?? DatabaseLiteral;
        var contractId = $"{ToIdentifier(sourceName)}-contract";
        var identity = _options.ContractIdentity ?? new ContractIdentity(contractId, sourceName);
        var metadata = _options.ContractMetadata ?? new ContractMetadata($"{sourceName} data contract");
        var extensions = ExtensionMetadata.For(ExtensionNamespace, new Dictionary<string, object?>
        {
            ["isComplete"] = inspection.IsComplete,
            ["coverage"] = inspection.Coverage.ToDictionary(
                item => item.Key.ToString(), item => (object?)item.Value.ToString(), StringComparer.Ordinal)
        });
        var contract = new DataContract(identity, metadata,
            assets.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray(), endpoints.Values.ToArray(),
            new ContractGovernance(), extensions);
        return new DataContractInferenceResult(contract, diagnostics);
    }

    private DataAsset MapTable(Table table, DatabaseSchema schema,
        IReadOnlyDictionary<string, string> assetIds,
        IReadOnlyDictionary<string, DatabaseObjectName> relations,
        IReadOnlyDictionary<DatabaseScope, DataEndpoint> endpoints,
        List<ContractInferenceDiagnostic> diagnostics, string sourcePath)
    {
        var overrides = AssetOverrides(table.Identity);
        var primaryKey = table.Constraints.OfType<PrimaryKeyConstraint>().SingleOrDefault()
            ?.Columns.Keys.ToArray() ?? [];
        var relationships = table.Constraints.OfType<DatabaseForeignKeyConstraint>()
            .Where(key => relations.ContainsKey(key.TargetTable.Key))
            .Select(key => new DataRelationship(key.SourceColumns,
                assetIds[key.TargetTable.Key], key.TargetColumns)).ToArray();
        var indexes = schema.Indexes.Values.Where(index => index.TableIdentity.Key == table.Identity.Key).ToArray();
        var checks = table.Constraints.OfType<CheckConstraint>().ToArray();
        var fields = table.Columns.Values.Select(column =>
            MapColumn(column, indexes, checks, diagnostics, sourcePath, table.Identity)).ToArray();
        var endpoint = endpoints[Scope(table.Identity)];
        return new DataAsset(
            assetIds[table.Identity.Key],
            overrides?.Name ?? table.Name,
            table.Identity.ToString(),
            overrides?.Description ?? table.Description,
            AssetKind.Table,
            new DataSchema(fields, primaryKey, relationships),
            [new EndpointBinding(endpoint.Id, table.Name)],
            Extensions: MapAssetExtensions(table.Identity, indexes, checks));
    }

    private DataAsset MapView(MetadataView view,
        IReadOnlyDictionary<string, string> assetIds,
        IReadOnlyDictionary<DatabaseScope, DataEndpoint> endpoints,
        List<ContractInferenceDiagnostic> diagnostics, string sourcePath)
    {
        var identity = Name(view.Identity);
        var overrides = AssetOverrides(identity);
        var fields = view.Columns.Select(column => MapColumn(column, diagnostics, sourcePath, identity)).ToArray();
        var endpoint = endpoints[Scope(identity)];
        return new DataAsset(
            assetIds[identity.Key],
            overrides?.Name ?? identity.Name,
            identity.ToString(),
            overrides?.Description,
            AssetKind.View,
            new DataSchema(fields),
            [new EndpointBinding(endpoint.Id, identity.Name)],
            Extensions: MapAssetExtensions(identity, [], []));
    }

    private static DataField MapColumn(Column column, IReadOnlyList<DatabaseIndex> indexes,
        IReadOnlyList<CheckConstraint> checks, List<ContractInferenceDiagnostic> diagnostics,
        string sourcePath, DatabaseObjectName table)
    {
        var (logicalType, format) = MapType(column.Type);
        var constraints = new List<DataConstraint>();
        if (column.DefaultValue is not null) constraints.Add(new("default", column.DefaultValue));
        if (column is VarLengthColumn length) constraints.Add(new("maxLength", length.Length));
        if (column is NumericColumn numeric)
        {
            constraints.Add(new("precision", numeric.Precision));
            constraints.Add(new("scale", numeric.Scale));
        }
        if (indexes.Any(index => index.IsUnique && index.Columns.Count == 1
            && index.Columns.ContainsKey(column.Name)))
            constraints.Add(new("unique", true));
        foreach (var check in checks)
            if (TryMapCheck(check, column.Name, out var mapped)) constraints.Add(mapped);
            else if (References(check, column.Name)) diagnostics.Add(new(
                "DBI002", InferenceSeverity.Information, sourcePath,
                $"Check constraint on '{table}.{column.Name}' was preserved in extension metadata."));

        var required = column.Constraints.OfType<NotNullableConstraint>().Any();
        return new DataField(column.Name, logicalType, column.NativeType?.Name ?? column.Type.ToString(),
            format, required, constraints, Extensions: MapColumnExtensions(column), Nullable: !required);
    }

    private static DataField MapColumn(MetadataColumn column,
        List<ContractInferenceDiagnostic> diagnostics, string sourcePath, DatabaseObjectName view)
    {
        if (column.Type is not { } dbType)
        {
            diagnostics.Add(new("DBI003", InferenceSeverity.Warning, sourcePath,
                $"View column '{view}.{column.Name}' has no discovered logical type."));
            return new DataField(column.Name, null, column.NativeType, Required: column.Nullable == false,
                Extensions: MapColumnExtensions(column), Nullable: column.Nullable ?? false);
        }
        var (logicalType, format) = MapType(dbType);
        var parameters = new List<DataConstraint>();
        if (column.Precision is not null) parameters.Add(new("precision", column.Precision));
        if (column.Scale is not null) parameters.Add(new("scale", column.Scale));
        return new DataField(column.Name, logicalType, column.NativeType ?? dbType.ToString(), format,
            column.Nullable == false, Constraints: parameters, Extensions: MapColumnExtensions(column),
            Nullable: column.Nullable ?? false);
    }

    private Dictionary<DatabaseScope, DataEndpoint> BuildEndpoints(
        IReadOnlyList<DatabaseObjectName> identities, ConnectionLocation location)
    {
        var scopes = identities.Select(Scope).Distinct().OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        var endpoints = new Dictionary<DatabaseScope, DataEndpoint>();
        var baseId = _options.EndpointId
            ?? $"{ToIdentifier(location.Database ?? location.Host ?? DatabaseLiteral)}-source";
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in scopes)
        {
            var candidate = scopes.Length == 1 ? baseId : $"{baseId}-{ToIdentifier(scope.Key)}";
            var endpointId = Unique(candidate, usedIds);
            var options = new Dictionary<string, object?>();
            if (scope.Catalog is not null) options["catalog"] = scope.Catalog;
            if (scope.Database is not null) options["databaseQualifier"] = scope.Database;
            var endpointLocation = location with
            {
                Database = scope.Database ?? location.Database,
                Namespace = scope.Schema
            };
            endpoints.Add(scope, new DataEndpoint(
                endpointId,
                _options.EndpointName ?? $"{scope.DisplayName} source",
                EndpointKind.Database,
                _options.Environment,
                endpointLocation,
                new DataFormat(DatabaseLiteral, Options: options)));
        }
        return endpoints;
    }

    private IReadOnlyDictionary<string, string> BuildAssetIds(IEnumerable<DatabaseObjectName> identities)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identity in identities)
        {
            var configured = AssetOverrides(identity)?.Id;
            var candidate = configured ?? ToIdentifier(identity.ToString());
            result[identity.Key] = Unique(candidate, used);
        }
        return result;
    }

    private DatabaseAssetInferenceOptions? AssetOverrides(DatabaseObjectName identity) =>
        _options.Assets.TryGetValue(identity.ToString(), out var value) ? value : null;

    private static List<ContractInferenceDiagnostic> MapDiagnostics(
        SchemaInspectionResult inspection, string sourcePath) =>
        inspection.Diagnostics.Select(diagnostic => new ContractInferenceDiagnostic(
            diagnostic.Code,
            MapSeverity(inspection.IsSuccess, diagnostic.Coverage),
            sourcePath,
            diagnostic.Object is null
                ? diagnostic.Message
                : $"{diagnostic.Message} Object: {diagnostic.Object}.")).ToList();

    private static InferenceSeverity MapSeverity(bool isSuccess, MetadataCoverage coverage)
    {
        if (!isSuccess) return InferenceSeverity.Error;
        return coverage is MetadataCoverage.Available or MetadataCoverage.Absent
            ? InferenceSeverity.Information
            : InferenceSeverity.Warning;
    }

    private static ExtensionMetadata MapAssetExtensions(DatabaseObjectName identity,
        IReadOnlyList<DatabaseIndex> indexes, IReadOnlyList<CheckConstraint> checks)
    {
        var values = new Dictionary<string, object?>
        {
            ["catalog"] = identity.Catalog,
            [DatabaseLiteral] = identity.Database,
            ["schema"] = identity.Schema
        };
        if (indexes.Count > 0)
            values["indexes"] = indexes.Select(index => new Dictionary<string, object?>
            {
                ["name"] = index.Identity.ToString(),
                ["unique"] = index.IsUnique,
                ["columns"] = index.Columns.Keys.ToArray()
            }).ToArray();
        if (checks.Count > 0)
            values["checks"] = checks.Select(check => new Dictionary<string, object?>
            {
                ["name"] = check.Name,
                ["left"] = Describe(check.Left),
                ["operator"] = check.Operator,
                ["right"] = Describe(check.Right)
            }).ToArray();
        return ExtensionMetadata.For(ExtensionNamespace, values);
    }

    private static ExtensionMetadata MapColumnExtensions(Column column)
    {
        var values = new Dictionary<string, object?>();
        if (column.Description is not null) values["description"] = column.Description;
        if (column.Generation is IdentityColumnGeneration identity)
        {
            values["generation"] = "identity";
            values["seed"] = identity.Seed;
            values["increment"] = identity.Increment;
        }
        else if (column.Generation is GeneratedColumnGeneration generated)
        {
            values["generation"] = "computed";
            values["storage"] = generated.Storage.ToString();
            values["expression"] = Describe(generated.Expression);
        }
        if (column.NativeType is not null)
            values["nativeTypeDialects"] = column.NativeType.Dialects
                .Select(x => x.FullName ?? x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return ExtensionMetadata.For(ExtensionNamespace, values);
    }

    private static ExtensionMetadata MapColumnExtensions(MetadataColumn column)
    {
        var values = new Dictionary<string, object?>();
        if (column.Comment is not null) values["description"] = column.Comment;
        if (column.Length is not null) values["length"] = column.Length;
        if (column.Precision is not null) values["precision"] = column.Precision;
        if (column.Scale is not null) values["scale"] = column.Scale;
        if (column.Default is not null) values["default"] = column.Default;
        return ExtensionMetadata.For(ExtensionNamespace, values);
    }

    private static bool TryMapCheck(CheckConstraint check, string columnName, out DataConstraint constraint)
    {
        constraint = null!;
        if (ColumnName(check.Left) == columnName && Value(check.Right, out var right))
            return TryMapComparison(check.Operator, right, false, out constraint);
        if (ColumnName(check.Right) == columnName && Value(check.Left, out var left))
            return TryMapComparison(check.Operator, left, true, out constraint);
        return false;
    }

    private static bool TryMapComparison(string op, object value, bool reverse, out DataConstraint constraint)
    {
        constraint = null!;
        var normalized = reverse ? op switch
        {
            ">" => "<", ">=" => "<=", "<" => ">", "<=" => ">=", _ => op
        } : op;
        var kind = normalized switch
        {
            ">" => "exclusiveMinimum",
            ">=" => "minimum",
            "<" => "exclusiveMaximum",
            "<=" => "maximum",
            "=" or "==" => "enum",
            "<>" or "!=" => "notEqual",
            _ => null
        };
        if (kind is null) return false;
        constraint = new DataConstraint(kind, kind == "enum" ? new[] { value } : value);
        return true;
    }

    private static bool References(CheckConstraint check, string columnName) =>
        ColumnName(check.Left) == columnName || ColumnName(check.Right) == columnName;

    private static string? ColumnName(Expression expression) => expression switch
    {
        ColumnIdentityExpression value => value.Name,
        ColumnExpression value => value.Column.Name,
        _ => null
    };

    private static bool Value(Expression expression, out object value)
    {
        if (expression is ValueExpression literal)
        {
            value = literal.Value;
            return true;
        }
        value = null!;
        return false;
    }

    private static object Describe(Expression expression) => expression switch
    {
        ColumnIdentityExpression value => new Dictionary<string, object?>
            { ["kind"] = "column", ["name"] = value.Name },
        ColumnExpression value => new Dictionary<string, object?>
            { ["kind"] = "column", ["name"] = value.Column.Name },
        FunctionColumnIdentityExpression value => new Dictionary<string, object?>
            { ["kind"] = "function", ["name"] = value.Function, ["column"] = value.Name },
        FunctionExpression value => new Dictionary<string, object?>
            { ["kind"] = "function", ["name"] = value.Function,
                ["arguments"] = value.Expressions.Select(Describe).ToArray() },
        ValueExpression value => new Dictionary<string, object?>
            { ["kind"] = "value", ["value"] = value.Value },
        _ => new Dictionary<string, object?> { ["kind"] = expression.GetType().Name }
    };

    private static (string LogicalType, string? Format) MapType(DbType type) => type switch
    {
        DbType.Boolean => ("boolean", null),
        DbType.SByte => ("integer", "i8"),
        DbType.Byte => ("integer", "u8"),
        DbType.Int16 => ("integer", "i16"),
        DbType.UInt16 => ("integer", "u16"),
        DbType.Int32 => ("integer", "i32"),
        DbType.UInt32 => ("integer", "u32"),
        DbType.Int64 => ("integer", "i64"),
        DbType.UInt64 => ("integer", "u64"),
        DbType.Single => ("number", "fp32"),
        DbType.Double => ("number", "fp64"),
        DbType.Currency or DbType.Decimal or DbType.VarNumeric => ("number", null),
        DbType.Date => ("date", null),
        DbType.Time => ("time", null),
        DbType.DateTime or DbType.DateTime2 => ("datetime", null),
        DbType.DateTimeOffset => ("timestamp", null),
        DbType.Guid => (StringLogicalType, "uuid"),
        DbType.Binary => (StringLogicalType, "binary"),
        DbType.Object => ("object", null),
        DbType.Xml => (StringLogicalType, "xml"),
        _ => (StringLogicalType, null)
    };

    private static DatabaseObjectName Name(MetadataName name) =>
        new(name.Name, name.Schema, name.Database, name.Catalog);

    private static DatabaseScope Scope(DatabaseObjectName name) =>
        new(name.Catalog, name.Database, name.Schema);

    private static ConnectionLocation Sanitize(string url)
    {
        var sanitized = SanitizeUrl(url);
        if (!Uri.TryCreate(sanitized, UriKind.Absolute, out var uri))
            return new ConnectionLocation(url.Split(':', 2)[0], ConnectionUrl: sanitized);
        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        return new ConnectionLocation(uri.Scheme,
            string.IsNullOrWhiteSpace(uri.Host) ? null : uri.Host,
            uri.IsDefaultPort ? null : uri.Port,
            string.IsNullOrWhiteSpace(database) ? null : database,
            ConnectionUrl: sanitized);
    }

    private static ConnectionLocation Sanitize(ConnectionLocation location) =>
        location with { ConnectionUrl = location.ConnectionUrl is null ? null : SanitizeUrl(location.ConnectionUrl) };

    private static string SanitizeUrl(string url)
    {
        var marker = url.IndexOf("://", StringComparison.Ordinal);
        if (marker < 0) return SanitizeTail(url);
        var authorityStart = marker + 3;
        var remainder = url[authorityStart..];
        var authorityEnd = remainder.IndexOfAny(['/', '?', '#']);
        var authority = authorityEnd < 0 ? remainder : remainder[..authorityEnd];
        var tail = authorityEnd < 0 ? string.Empty : remainder[authorityEnd..];
        var userInfo = authority.LastIndexOf('@');
        if (userInfo >= 0) authority = authority[(userInfo + 1)..];
        return $"{url[..authorityStart]}{authority}{SanitizeTail(tail)}";
    }

    private static string SanitizeTail(string tail)
    {
        var fragment = tail.IndexOf('#');
        if (fragment >= 0) tail = tail[..fragment];
        var query = tail.IndexOf('?');
        if (query < 0) return tail;
        var parameters = tail[(query + 1)..].Split('&', StringSplitOptions.None)
            .Select(RedactQueryParameter);
        return $"{tail[..(query + 1)]}{string.Join('&', parameters)}";
    }

    private static string RedactQueryParameter(string parameter)
    {
        var separator = parameter.IndexOf('=');
        if (separator < 0 || !IsSensitiveQueryKey(parameter[..separator])) return parameter;
        return $"{parameter[..(separator + 1)]}{RedactedQueryValue}";
    }

    private static bool IsSensitiveQueryKey(string encodedKey)
    {
        string key;
        try
        {
            key = Uri.UnescapeDataString(encodedKey.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            key = encodedKey;
        }
        var normalized = new string(key.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant).ToArray());
        return SensitiveQueryKeys.Contains(normalized)
            || SensitiveQueryKeySuffixes.Any(suffix => normalized.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static string SafeSource(ConnectionLocation location) =>
        location.ConnectionUrl ?? $"{location.Scheme}://{location.Host ?? "."}/{location.Database ?? string.Empty}";

    private static string Unique(string candidate, ISet<string> used)
    {
        candidate = string.IsNullOrWhiteSpace(candidate) ? DatabaseLiteral : candidate;
        var value = candidate;
        var suffix = 2;
        while (!used.Add(value)) value = $"{candidate}-{suffix++}";
        return value;
    }

    private static string ToIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        var separator = false;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                separator = false;
            }
            else if (builder.Length > 0 && !separator)
            {
                builder.Append('-');
                separator = true;
            }
        }
        return builder.ToString().TrimEnd('-') is { Length: > 0 } identifier
            ? identifier
            : DatabaseLiteral;
    }

    private sealed record DatabaseScope(string? Catalog, string? Database, string? Schema)
    {
        public string Key => string.Join('.', new[] { Catalog, Database, Schema }.Where(x => x is not null));
        public string DisplayName => string.IsNullOrWhiteSpace(Key) ? DatabaseLiteral : Key;
    }

    private static readonly HashSet<string> SensitiveQueryKeys = new(StringComparer.Ordinal)
    {
        "password", "pwd", "pass", "token", "accesstoken", "refreshtoken", "idtoken", "authtoken",
        "sastoken", "apikey", "secret", "clientsecret", "credential", "credentials", "privatekey",
        "accesskey", "secretkey"
    };

    private static readonly string[] SensitiveQueryKeySuffixes =
        ["password", "secret", "token", "credential", "credentials", "privatekey", "apikey"];
}
