using System.Text.Json;

namespace Packata.Core.Contracts;

internal static class SpatialDeclarations
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static SpatialDescriptor? DataPackage(string? type, string? format) => type switch
    {
        "geopoint" => new(SubType: SpatialSubtype.Point, Dimensions: 2),
        "geojson" when format == "topojson" => new(Encoding: SpatialEncoding.TopoJson, Content: SpatialContent.Topology),
        "geojson" => new(Encoding: SpatialEncoding.GeoJson, Content: SpatialContent.GeoJsonValue),
        _ => null
    };

    internal static PhysicalTypeContext? SourceContext(IReadOnlyList<DataEndpoint> endpoints)
        => endpoints is [{ Kind: EndpointKind.Database, Location: ConnectionLocation location } endpoint]
            && location.Scheme is not ("unknown" or "custom")
            ? new(Provider: location.Scheme, Server: endpoint.Id) : null;

    internal static PhysicalTypeContext? PhysicalContext(object? declaration, PhysicalTypeContext? source,
        string path, ICollection<MappingDiagnostic> diagnostics)
    {
        if (declaration is null) return source;
        try
        {
            var context = JsonSerializer.SerializeToElement(declaration, JsonOptions).Deserialize<PhysicalTypeContext>(JsonOptions);
            if (context is null || (string.IsNullOrWhiteSpace(context.Provider) && string.IsNullOrWhiteSpace(context.Dialect)))
                throw new JsonException();
            if (source?.Provider is { } provider && context.Provider is { } declared
                && !string.Equals(provider, declared, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new("SPATIAL_PHYSICAL_CONTEXT_CONFLICT", MappingSeverity.Error, path,
                    "The explicit physical-type provider conflicts with the bound source endpoint."));
            }
            return context with { Provider = context.Provider ?? source?.Provider, Server = context.Server ?? source?.Server };
        }
        catch (JsonException)
        {
            diagnostics.Add(new("SPATIAL_PHYSICAL_CONTEXT", MappingSeverity.Error, path,
                "physicalTypeContext must be an object with an explicit provider or dialect."));
            return source;
        }
    }

    internal static void CheckPhysicalContext(DataField field, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        if (field.Spatial is not null && field.PhysicalType is not null && field.PhysicalTypeContext is null)
        {
            diagnostics.Add(new("SPATIAL_PHYSICAL_CONTEXT", MappingSeverity.Warning, path,
                "The spatial physical type has no unambiguous source provider or dialect. Native type compatibility requires explicit dialect resolution."));
        }
    }

    internal static void CheckRfc0048Content(SpatialDescriptor descriptor, string path, ICollection<MappingDiagnostic> diagnostics)
    {
        if (descriptor.Content == SpatialContent.Topology)
        {
            diagnostics.Add(new("SPATIAL_RFC0048_ENCODING", MappingSeverity.Warning, path,
                "TopoJSON topology is retained. The proposed ODCS RFC-0048 has no equivalent encoding."));
        }

        if (descriptor.Content == SpatialContent.GeoJsonValue)
        {
            diagnostics.Add(new("SPATIAL_RFC0048_CONTENT", MappingSeverity.Warning, path,
                "This GeoJSON declaration permits features and feature collections. Mapping it to an RFC-0048 geometry-only declaration requires an explicit content restriction."));
        }
    }
}
