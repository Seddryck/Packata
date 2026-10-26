using Packata.Core.Contracts;
using Packata.OpenDataContract.ServerTypes;
using Packata.OpenDataContract.Types;
using CoreContract = Packata.Core.Contracts.DataContract;

namespace Packata.OpenDataContract.Mapping;

public sealed class OpenDataContractMapper : IDataContractMapper<DataContract>
{
    public MappingResult<CoreContract> Map(DataContract document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<MappingDiagnostic>();
        var directory = DirectoryServer(document);
        var endpoints = MapEndpoints(document, directory, diagnostics);
        var canBindEndpoints = document.Servers.Count <= 1;
        if (!canBindEndpoints && document.Schema.Count > 0)
        {
            diagnostics.Add(new MappingDiagnostic(
                "ODCS001",
                MappingSeverity.Warning,
                "servers",
                "Multiple servers cannot be unambiguously assigned to schema objects; endpoints were retained without inferred bindings."));
        }

        var assets = document.Schema
            .Select(schema => MapAsset(schema, AssetEndpoints(schema, endpoints, directory, canBindEndpoints), diagnostics))
            .ToArray();
        assets = CategoryTableMapper.Map(document, assets, diagnostics);

        var extensions = MapCustomProperties(document.Description?.CustomProperties);
        var commitments = ServiceCommitmentMapper.Map(document, diagnostics);
        if (commitments.Unmapped.Count > 0)
        {
            var values = extensions.TryGetNamespace("odcs", out var existing)
                ? new Dictionary<string, object?>(existing)
                : new Dictionary<string, object?>();
            values["slaProperties"] = commitments.Unmapped;
            extensions = ExtensionMetadata.For("odcs", values);
        }

        var contract = new CoreContract(
            new ContractIdentity(document.Id, document.Name, document.Version, document.Status),
            new ContractMetadata(
                Description: document.Description?.Purpose,
                Domain: document.Domain,
                Tags: document.Tags),
            assets,
            endpoints,
            new ContractGovernance(
                Terms: new TermsOfUse(
                    document.Description?.Usage,
                    document.Description?.Limitations),
                References: document.AuthoritativeDefinitions
                    .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                    .Select(x => new AuthoritativeReference(x.Url!, x.Type))
                    .ToArray()),
            extensions,
            commitments.Sla,
            commitments.Lifecycle);

        return new MappingResult<CoreContract>(contract, diagnostics);
    }

    private static LocalFilesServer? DirectoryServer(DataContract document)
    {
        if (document.Servers.Count != 1 || document.Servers[0] is not LocalFilesServer local) return null;
        return local.Path.EndsWith('/') || local.Path.EndsWith('\\') ? local : null;
    }

    private static DataEndpoint[] MapEndpoints(DataContract document, LocalFilesServer? directory, ICollection<MappingDiagnostic> diagnostics)
    {
        if (directory is null) return document.Servers.Select(MapEndpoint).ToArray();
        foreach (var schema in document.Schema.Where(schema => string.IsNullOrWhiteSpace(schema.PhysicalName)))
            diagnostics.Add(new("ODCS006", MappingSeverity.Error, $"schema/{schema.Name}/physicalName",
                "A local directory server requires an explicit physicalName for each schema object."));
        return document.Schema.Where(schema => !string.IsNullOrWhiteSpace(schema.PhysicalName)).Select(schema =>
            MapEndpoint(directory) with { Id = $"{directory.Server}/{schema.Name}",
                Location = new PathLocation([Path.Combine(directory.Path, schema.PhysicalName!)]) }).ToArray();
    }

    private static IReadOnlyList<DataEndpoint> AssetEndpoints(SchemaObject schema, DataEndpoint[] endpoints,
        LocalFilesServer? directory, bool canBind)
    {
        if (directory is not null)
            return endpoints.Where(endpoint => endpoint.Id == $"{directory.Server}/{schema.Name}").ToArray();
        return canBind ? endpoints : [];
    }

    private static DataAsset MapAsset(SchemaObject schema, IReadOnlyList<DataEndpoint> endpoints,
        ICollection<MappingDiagnostic> diagnostics)
    {
        var fields = schema.Properties.Select(property => MapField(property, $"schema/{schema.Name}/properties/{property.Name}", diagnostics, schema.Name, [property.Name])).ToArray();
        var primaryKey = schema.Properties
            .Where(x => x.PrimaryKey)
            .OrderBy(x => x.PrimaryKeyPosition ?? int.MaxValue)
            .Select(x => x.Name)
            .ToArray();

        var relationships = schema.Relationships
            .Select(value => MapRelationship(value, schema.Name, null, diagnostics))
            .Concat(schema.Properties.SelectMany(property => property.Relationships
                .Select(value => MapRelationship(value, schema.Name, property.Name, diagnostics))))
            .Where(value => value is not null).Cast<DataRelationship>().ToArray();

        return new DataAsset(
            schema.Name,
            schema.Name,
            schema.PhysicalName,
            schema.Description,
            MapAssetKind(schema.PhysicalType),
            new DataSchema(fields, primaryKey, relationships),
            endpoints.Select(x => new EndpointBinding(x.Id, schema.PhysicalName)).ToArray(),
            QualityRules: QualityMapper.Map(schema.Quality, new QualityTarget(schema.Name), $"schema/{schema.Name}/quality", diagnostics),
            Extensions: MapCustomProperties(schema.CustomProperties));
    }

    private static DataRelationship? MapRelationship(Relationship relationship, string sourceAsset,
        string? sourceField, ICollection<MappingDiagnostic> diagnostics)
    {
        var from = ParseReference(relationship.From?.ToString(), sourceAsset, sourceField);
        var to = ParseReference(relationship.To.ToString(), null, null);
        if (from is null || to is null || string.IsNullOrWhiteSpace(from.Value.Field)
            || string.IsNullOrWhiteSpace(to.Value.Asset) || string.IsNullOrWhiteSpace(to.Value.Field))
        {
            diagnostics.Add(new("ODCS003", MappingSeverity.Warning, $"schema/{sourceAsset}/relationships",
                $"Relationship '{relationship.Id ?? "<unnamed>"}' has an unsupported reference path."));
            return null;
        }

        if (!relationship.Type.Equals("foreignKey", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(new("ODCS002", MappingSeverity.Warning, $"schema/{sourceAsset}/relationships",
                $"Relationship type '{relationship.Type}' was retained but will not be provisioned as a foreign key."));

        return new([from.Value.Field], to.Value.Asset, [to.Value.Field], relationship.Id,
            relationship.Type, MapCustomProperties(relationship.CustomProperties));
    }

    private static (string? Asset, string? Field)? ParseReference(string? value,
        string? defaultAsset, string? defaultField)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultAsset is null ? null : (defaultAsset, defaultField);
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4 && parts[0].Equals("schema", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("properties", StringComparison.OrdinalIgnoreCase))
            return (parts[1], parts[3]);
        parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            >= 2 => (parts[^2], parts[^1]),
            1 when defaultAsset is not null => (defaultAsset, parts[0]),
            _ => null
        };
    }

    private static DataField MapField(SchemaProperty property, string path, ICollection<MappingDiagnostic> diagnostics, string assetId, IReadOnlyList<string> fieldPath)
    {
        var domain = MapValueDomain(property, path, diagnostics);
        var constraints = MapConstraints(property);
        if (domain?.AllowedValues is not null)
            constraints = constraints.Where(value => value.Kind != "enum").ToArray();
        return new DataField(
            property.Name,
            GetLogicalTypeName(property.LogicalType),
            property.PhysicalType,
            GetFormat(property.LogicalType),
            property.Required ?? false,
            constraints,
            QualityRules: QualityMapper.Map(property.Quality, new QualityTarget(assetId, fieldPath), $"{path}/quality", diagnostics),
            Children: property.Properties.Select(child => MapField(child, $"{path}/properties/{child.Name}", diagnostics, assetId, [..fieldPath, child.Name])).ToArray(),
            Extensions: MapFieldExtensionMetadata(property),
            PhysicalName: property.PhysicalName,
            Description: property.Description,
            Nullable: property.Nullable ?? false,
            Items: property.Items is null ? null : MapField(property.Items, $"{path}/items", diagnostics, assetId, [..fieldPath, "[]"]),
            ValueDomain: domain);
    }

    private static ValueDomain? MapValueDomain(SchemaProperty property, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        if (property.Enum is null) return null;
        var entries = new List<ValueOption>();
        for (var index = 0; index < property.Enum.Count; index++)
        {
            var entry = property.Enum[index];
            if (!ScalarValue.TryCreate(entry.Value, out var value) || value.Kind == ScalarKind.Null || !value.IsCompatibleWith(GetLogicalTypeName(property.LogicalType)))
            {
                diagnostics.Add(new("ODCS004", MappingSeverity.Warning, $"{path}/enum/{index}/value",
                    "The enum value is non-scalar, outside the supported scalar range, or incompatible with the field type; the original constraint and metadata were retained without a scalar domain."));
                return null;
            }
            var metadata = EnumMetadataSnapshot.Entry(entry).Where(pair => pair.Key is not ("value" or "label" or "description"));
            entries.Add(new ValueOption(value, entry.Label, entry.Description,
                ExtensionMetadata.For("odcs", metadata.ToDictionary(pair => pair.Key, pair => pair.Value))));
        }
        try
        {
            if (entries.Count == 0) throw new ArgumentException("ODCS enum requires at least one entry.");
            return new ValueDomain(entries, entries.Select(entry => entry.Value));
        }
        catch (ArgumentException exception)
        {
            diagnostics.Add(new("ODCS004", MappingSeverity.Warning, $"{path}/enum", exception.Message));
            return null;
        }
    }

    private static IReadOnlyList<DataConstraint> MapConstraints(SchemaProperty property)
    {
        var constraints = new List<DataConstraint>();
        if (property.PrimaryKey) constraints.Add(new("primaryKey", true));
        if (property.Unique == true) constraints.Add(new("unique", true));
        AddRangeAndLengthConstraints(property.LogicalType, constraints);
        if (property.Enum is { Count: > 0 })
            constraints.Add(new("enum", property.Enum.Select(value => MetadataSnapshot.CopyValue(value.Value)).ToArray()));
        return constraints;
    }

    private static Dictionary<string, object?> MapFieldExtensions(SchemaProperty property)
    {
        var extensions = new Dictionary<string, object?>();
        if (property.Classification is not null) extensions["classification"] = property.Classification;
        if (property.Partitioned is not null) extensions["partitioned"] = property.Partitioned;
        if (property.PartitionKeyPosition is not null)
            extensions["partitionKeyPosition"] = property.PartitionKeyPosition;
        if (property.CustomProperties.Count > 0)
            extensions["customProperties"] = MapCustomPropertyValues(property.CustomProperties);
        if (property.Enum is not null)
            extensions["enum"] = EnumMetadataSnapshot.Copy(property.Enum);
        return extensions;
    }

    private static ExtensionMetadata MapFieldExtensionMetadata(SchemaProperty property)
    {
        var extensions = MapFieldExtensions(property);
        return extensions.Count == 0 ? ExtensionMetadata.Empty : ExtensionMetadata.For("odcs", extensions);
    }

    private static void AddRangeAndLengthConstraints(ILogicalType? logicalType,
        ICollection<DataConstraint> constraints)
    {
        switch (logicalType)
        {
            case StringLogicalType text:
                AddStringConstraints(text, constraints);
                break;
            case NumberLogicalType number:
                if (number.Precision is { } precision) constraints.Add(new("precision", precision));
                if (number.Scale is { } scale) constraints.Add(new("scale", scale));
                AddNumberConstraints(number.Minimum, number.Maximum, number.ExclusiveMinimum,
                    number.ExclusiveMaximum, constraints);
                break;
            case IntegerLogicalType integer:
                AddNumberConstraints(integer.Minimum, integer.Maximum, integer.ExclusiveMinimum,
                    integer.ExclusiveMaximum, constraints);
                break;
            case ArrayLogicalType array:
                AddArrayConstraints(array, constraints);
                break;
            case ObjectLogicalType obj:
                AddObjectConstraints(obj, constraints);
                break;
            case DateLogicalType date:
                AddNumberConstraints(date.Minimum, date.Maximum, date.ExclusiveMinimum,
                    date.ExclusiveMaximum, constraints);
                break;
            case TimestampLogicalType timestamp:
                AddNumberConstraints(timestamp.Minimum, timestamp.Maximum, timestamp.ExclusiveMinimum,
                    timestamp.ExclusiveMaximum, constraints);
                break;
            case TimeLogicalType time:
                AddNumberConstraints(time.Minimum, time.Maximum, time.ExclusiveMinimum,
                    time.ExclusiveMaximum, constraints);
                break;
        }
    }

    private static void AddStringConstraints(StringLogicalType text, ICollection<DataConstraint> constraints)
    {
        if (text.MinLength is not null) constraints.Add(new("minLength", text.MinLength));
        if (text.MaxLength is not null) constraints.Add(new("maxLength", text.MaxLength));
        if (!string.IsNullOrWhiteSpace(text.Pattern)) constraints.Add(new("pattern", text.Pattern));
    }

    private static void AddArrayConstraints(ArrayLogicalType array, ICollection<DataConstraint> constraints)
    {
        if (array.MinItems is not null) constraints.Add(new("minItems", array.MinItems));
        if (array.MaxItems is not null) constraints.Add(new("maxItems", array.MaxItems));
        if (array.UniqueItems) constraints.Add(new("uniqueItems", true));
    }

    private static void AddObjectConstraints(ObjectLogicalType obj, ICollection<DataConstraint> constraints)
    {
        if (obj.MinProperties is not null) constraints.Add(new("minProperties", obj.MinProperties));
        if (obj.MaxProperties is not null) constraints.Add(new("maxProperties", obj.MaxProperties));
        if (obj.Required.Length > 0) constraints.Add(new("required", obj.Required));
    }

    private static void AddNumberConstraints(object? minimum, object? maximum, bool exclusiveMinimum,
        bool exclusiveMaximum, ICollection<DataConstraint> constraints)
    {
        if (minimum is not null)
            constraints.Add(new(exclusiveMinimum ? "exclusiveMinimum" : "minimum", minimum));
        if (maximum is not null)
            constraints.Add(new(exclusiveMaximum ? "exclusiveMaximum" : "maximum", maximum));
    }

    private static DataEndpoint MapEndpoint(BaseServer server)
    {
        var location = MapLocation(server);
        var formatName = GetFormatName(server);
        var encoding = (server as IEncodingAware)?.Encoding;
        var formatOptions = MapFormatOptions(server, formatName);

        return new DataEndpoint(
            server.Server,
            server.Description,
            MapEndpointKind(server),
            server.Environment,
            location,
            formatName is not null || encoding is not null
                ? new DataFormat(formatName, Encoding: encoding, Options: formatOptions)
                : null,
            MapCustomProperties(server.CustomProperties));
    }

    private static DataLocation MapLocation(BaseServer server) => server switch
    {
        LocalFilesServer local => new PathLocation([local.Path]),
        CustomServer custom when HasConnectionMetadata(custom) => MapCustomLocation(custom),
        ILocationAware located => new PathLocation([located.Location]),
        IHostAware hosted => MapHostedLocation(server, hosted),
        IDatabaseAware database => MapDatabaseLocation(server, database),
        _ => new ConnectionLocation(server.Type)
    };

    private static bool HasConnectionMetadata(CustomServer server) => server.Host is not null
        || server.Database is not null || server.Schema is not null || server.Catalog is not null;

    private static ConnectionLocation MapCustomLocation(CustomServer server) => new(
        server.Type, server.Host, ConvertPort(server.Port), server.Database, server.Schema,
        server.EndpointUrl, server.Catalog);

    private static ConnectionLocation MapHostedLocation(BaseServer server, IHostAware hosted) => new(
        server.Type, hosted.Host, ConvertPort(hosted.Port), (server as IDatabaseAware)?.Database,
        (server as ISchemaAware)?.Schema, Catalog: (server as ICatalogAware)?.Catalog);

    private static ConnectionLocation MapDatabaseLocation(BaseServer server, IDatabaseAware database) => new(
        server.Type, Database: database.Database, Namespace: (server as ISchemaAware)?.Schema,
        Catalog: (server as ICatalogAware)?.Catalog);

    private static string? GetFormatName(BaseServer server) => server switch
    {
        IFormatAware formatted => formatted.Format,
        CustomServer custom => custom.Format,
        _ => null
    };

    private static IReadOnlyDictionary<string, object?> MapFormatOptions(BaseServer server, string? formatName)
    {
        var options = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!IsDelimitedFormat(formatName) && formatName?.Trim().TrimStart('.').ToLowerInvariant() is not ("xls" or "xlsx")) return options;

        if (server is CustomServer { Delimiter: not null } custom)
            options["delimiter"] = custom.Delimiter;

        foreach (var property in server.CustomProperties.Where(property =>
                     string.Equals(property.Vendor, "packata", StringComparison.OrdinalIgnoreCase)))
        {
            var optionName = property.Property?.ToLowerInvariant() switch
            {
                "delimiter" => "delimiter",
                "quotechar" => "quoteChar",
                "lineterminator" => "lineTerminator",
                "header" => "header",
                "sheetname" => "sheetName",
                "sheetnumber" => "sheetNumber",
                "headerrows" => "headerRows",
                "commentrows" => "commentRows",
                _ => null
            };
            if (optionName is not null) options[optionName] = property.Value;
        }

        return options;
    }

    private static bool IsDelimitedFormat(string? formatName) =>
        formatName?.Trim().TrimStart('.').ToLowerInvariant() is "csv" or "tsv" or "psv";

    private static ExtensionMetadata MapCustomProperties(CustomProperties? properties)
        => properties is null || properties.Count == 0
            ? ExtensionMetadata.Empty
            : ExtensionMetadata.For(
                "odcs",
                new Dictionary<string, object?>
                {
                    ["customProperties"] = MapCustomPropertyValues(properties)
                });

    private static Dictionary<string, object?>[] MapCustomPropertyValues(CustomProperties properties) =>
        properties.Select(x => new Dictionary<string, object?>
        {
            ["id"] = x.Id,
            ["property"] = x.Property,
            ["value"] = MetadataSnapshot.CopyValue(x.Value),
            ["description"] = x.Description,
            ["vendor"] = x.Vendor
        }).ToArray();

    private static int? ConvertPort(object? port)
        => port switch
        {
            int value => value,
            long value when value is >= int.MinValue and <= int.MaxValue => (int)value,
            string value when int.TryParse(value, out var parsed) => parsed,
            _ => null
        };

    private static AssetKind MapAssetKind(string? type)
        => type?.ToLowerInvariant() switch
        {
            "table" => AssetKind.Table,
            "view" => AssetKind.View,
            "topic" => AssetKind.Topic,
            "file" => AssetKind.File,
            "object" => AssetKind.Object,
            _ => AssetKind.Unknown
        };

    private static EndpointKind MapEndpointKind(BaseServer server)
        => server.Type.ToLowerInvariant() switch
        {
            "local" => EndpointKind.File,
            "s3" or "azure" => EndpointKind.ObjectStorage,
            "kafka" => EndpointKind.Stream,
            "api" => EndpointKind.Api,
            _ when server is IDatabaseAware || server is IHostAware => EndpointKind.Database,
            _ => EndpointKind.Unknown
        };

    private static string? GetLogicalTypeName(ILogicalType? type)
        => type switch
        {
            null or UnspecifiedLogicalType => null,
            UnknownLogicalType unknown => unknown.Type,
            StringLogicalType => "string",
            DateLogicalType => "date",
            TimestampLogicalType => "timestamp",
            TimeLogicalType => "time",
            NumberLogicalType => "number",
            IntegerLogicalType => "integer",
            ObjectLogicalType => "object",
            ArrayLogicalType => "array",
            BooleanLogicalType => "boolean",
            MapLogicalType => "map",
            VectorLogicalType => "vector",
            _ => null
        };

    private static string? GetFormat(ILogicalType? type)
        => type switch
        {
            StringLogicalType value => value.Format,
            DateLogicalType value => value.Format,
            TimestampLogicalType value => value.Format,
            TimeLogicalType value => value.Format,
            NumberLogicalType value => value.Format,
            IntegerLogicalType value => value.Format,
            _ => null
        };
}
