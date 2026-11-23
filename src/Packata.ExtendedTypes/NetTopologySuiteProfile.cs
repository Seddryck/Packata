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

        var resolution = FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict);
        var error = resolution.Diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Target == TypeMappingTarget.Source && diagnostic.Severity == TypeMappingSeverity.Error);
        if (error is not null)
            throw new NotSupportedException($"NetTopologySuite cannot bind field '{field.Name}'. {error.Message}");

        return logicalType switch
        {
            "geopoint" => typeof(Point),
            "geojson" when field.Format?.Trim().Equals("topojson", StringComparison.OrdinalIgnoreCase) != true
                => typeof(Geometry),
            "geometry" or "geography" when field.Spatial?.Encoding is SpatialEncoding.Wkt or SpatialEncoding.GeoJson
                => GetGeometryType(field.Spatial.SubType),
            "geometry" or "geography" => throw new NotSupportedException(
                $"NetTopologySuite has no binding for field '{field.Name}' with encoding '{field.Spatial?.Encoding?.ToString() ?? "unspecified"}'."),
            _ => null
        };
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
