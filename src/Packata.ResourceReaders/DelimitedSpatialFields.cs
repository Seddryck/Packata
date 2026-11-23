using System.Text.Json;
using Packata.Core;
using Packata.Core.Contracts;

namespace Packata.ResourceReaders;

internal static class DelimitedSpatialFields
{
    internal static Type? GetDefaultRepresentation(DataField field)
    {
        var logicalType = field.LogicalType?.Trim().ToLowerInvariant();
        var supported = logicalType is "geopoint" or "geojson"
            || (logicalType is "geometry" or "geography" && field.Spatial?.Encoding is SpatialEncoding.Wkt or SpatialEncoding.GeoJson);
        if (!supported) return null;

        var resolution = FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict);
        var diagnostic = resolution.Diagnostics.FirstOrDefault(item =>
            item.Severity == TypeMappingSeverity.Error && item.Target == TypeMappingTarget.Source);
        if (diagnostic is not null)
            throw new NotSupportedException($"Cannot bind CSV field '{field.Name}'. {diagnostic.Message}");

        return resolution.RuntimeType
            ?? throw new NotSupportedException($"Cannot bind CSV field '{field.Name}' as '{logicalType}'.");
    }

    internal static DelimitedFieldBinding? TryBind(ReaderRepresentationRequest request)
    {
        if (request.RuntimeType == typeof(GeoPoint)
            && string.Equals(request.Field.LogicalType?.Trim(), "geopoint", StringComparison.OrdinalIgnoreCase))
        {
            return new((schema, field, type) =>
            {
                var format = GetGeoPointFormat(field);
                schema.WithCustomField(type, field.Name,
                    builder => builder.WithDataSourceTypeName("geopoint")
                        .WithParser<GeoPoint>(text => GeoPointParser.Parse(text, format)));
            });
        }

        if (request.RuntimeType == typeof(JsonElement)
            && (string.Equals(request.Field.LogicalType?.Trim(), "geojson", StringComparison.OrdinalIgnoreCase)
                || request.Encoding == SpatialEncoding.GeoJson))
        {
            return new((schema, field, type) => schema.WithCustomField(type, field.Name,
                builder => builder.WithDataSourceTypeName(field.LogicalType!)
                    .WithParser<JsonElement>(text => JsonSerializer.Deserialize<JsonElement>(text))));
        }

        return null;
    }

    internal static GeoPointFormat GetGeoPointFormat(DataField field) => field.Format?.Trim().ToLowerInvariant() switch
    {
        "array" => GeoPointFormat.Array,
        "object" => GeoPointFormat.Object,
        _ => GeoPointFormat.Default
    };
}
