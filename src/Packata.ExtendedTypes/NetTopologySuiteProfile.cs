using NetTopologySuite.Geometries;
using Packata.Core;
using Packata.Core.Contracts;

namespace Packata.ExtendedTypes;

internal static class NetTopologySuiteProfile
{
    internal static Type? SelectType(DataField field)
    {
        var logicalType = field.LogicalType?.Trim().ToLowerInvariant();
        if (logicalType is not ("geopoint" or "geojson" or "geometry" or "geography")) return null;

        ValidateDeclaration(field);
        return logicalType switch
        {
            "geopoint" => typeof(Point),
            "geojson" => SelectGeoJsonType(field.Format),
            _ => SelectGeometryType(field)
        };
    }

    private static void ValidateDeclaration(DataField field)
    {
        var resolution = FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict);
        var error = resolution.Diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Target == TypeMappingTarget.Source && diagnostic.Severity == TypeMappingSeverity.Error);
        if (error is not null)
            throw new NotSupportedException($"NetTopologySuite cannot bind field '{field.Name}'. {error.Message}");
    }

    private static Type? SelectGeoJsonType(string? format)
        => string.Equals(format?.Trim(), "topojson", StringComparison.OrdinalIgnoreCase) ? null : typeof(Geometry);

    private static Type SelectGeometryType(DataField field)
    {
        var spatial = field.Spatial;
        if (spatial?.Encoding is SpatialEncoding.Wkt or SpatialEncoding.GeoJson)
            return GetGeometryType(spatial.SubType);

        var encoding = spatial?.Encoding?.ToString() ?? "unspecified";
        throw new NotSupportedException($"NetTopologySuite has no binding for field '{field.Name}' with encoding '{encoding}'.");
    }

    private static Type GetGeometryType(SpatialSubtype? subtype) => subtype switch
    {
        SpatialSubtype.Point => typeof(Point),
        SpatialSubtype.LineString => typeof(LineString),
        SpatialSubtype.Polygon => typeof(Polygon),
        SpatialSubtype.MultiPoint => typeof(MultiPoint),
        SpatialSubtype.MultiLineString => typeof(MultiLineString),
        SpatialSubtype.MultiPolygon => typeof(MultiPolygon),
        SpatialSubtype.GeometryCollection => typeof(GeometryCollection),
        _ => typeof(Geometry)
    };
}
