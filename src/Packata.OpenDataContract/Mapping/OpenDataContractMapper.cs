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
        var endpoints = document.Servers.Select(MapEndpoint).ToArray();
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
            .Select(schema => MapAsset(schema, canBindEndpoints ? endpoints : []))
            .ToArray();

        var extensions = MapCustomProperties(document.Description?.CustomProperties);

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
            extensions);

        return new MappingResult<CoreContract>(contract, diagnostics);
    }

    private static DataAsset MapAsset(SchemaObject schema, IReadOnlyList<DataEndpoint> endpoints)
    {
        var fields = schema.Properties.Select(MapField).ToArray();
        var primaryKey = schema.Properties
            .Where(x => x.PrimaryKey)
            .OrderBy(x => x.PrimaryKeyPosition ?? int.MaxValue)
            .Select(x => x.Name)
            .ToArray();

        return new DataAsset(
            schema.Name,
            schema.Name,
            schema.PhysicalName,
            schema.Description,
            MapAssetKind(schema.PhysicalType),
            new DataSchema(fields, primaryKey),
            endpoints.Select(x => new EndpointBinding(x.Id, schema.PhysicalName)).ToArray());
    }

    private static DataField MapField(SchemaProperty property)
    {
        var constraints = new List<DataConstraint>();
        if (property.PrimaryKey)
            constraints.Add(new DataConstraint("primaryKey", true));
        if (property.Unique == true)
            constraints.Add(new DataConstraint("unique", true));

        var extensions = new Dictionary<string, object?>();
        if (property.Classification is not null)
            extensions["classification"] = property.Classification;
        if (property.Partitioned is not null)
            extensions["partitioned"] = property.Partitioned;
        if (property.PartitionKeyPosition is not null)
            extensions["partitionKeyPosition"] = property.PartitionKeyPosition;
        if (property.CustomProperties.Count > 0)
            extensions["customProperties"] = MapCustomPropertyValues(property.CustomProperties);

        return new DataField(
            property.Name,
            GetLogicalTypeName(property.LogicalType),
            property.PhysicalType,
            GetFormat(property.LogicalType),
            property.Required ?? false,
            constraints,
            Extensions: extensions.Count == 0
                ? ExtensionMetadata.Empty
                : ExtensionMetadata.For("odcs", extensions));
    }

    private static DataEndpoint MapEndpoint(BaseServer server)
    {
        DataLocation location = server switch
        {
            LocalFilesServer local => new PathLocation([local.Path]),
            ILocationAware located => new PathLocation([located.Location]),
            IHostAware hosted => new ConnectionLocation(
                server.Type,
                hosted.Host,
                ConvertPort(hosted.Port),
                (server as IDatabaseAware)?.Database,
                (server as ISchemaAware)?.Schema),
            IDatabaseAware database => new ConnectionLocation(
                server.Type,
                Database: database.Database,
                Namespace: (server as ISchemaAware)?.Schema),
            _ => new ConnectionLocation(server.Type)
        };

        var formatName = server switch
        {
            IFormatAware formatted => formatted.Format,
            CustomServer custom => custom.Format,
            _ => null
        };
        var encoding = (server as IEncodingAware)?.Encoding;

        return new DataEndpoint(
            server.Server,
            server.Description,
            MapEndpointKind(server),
            server.Environment,
            location,
            formatName is not null || encoding is not null
                ? new DataFormat(formatName, Encoding: encoding)
                : null,
            MapCustomProperties(server.CustomProperties));
    }

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
            ["value"] = x.Value,
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
