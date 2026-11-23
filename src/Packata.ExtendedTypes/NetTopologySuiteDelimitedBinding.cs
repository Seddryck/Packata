using NetTopologySuite.Geometries;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.ResourceReaders;
using PocketCsvReader.Configuration;
using PocketCsvReader.NetTopologySuite;

namespace Packata.ExtendedTypes;

/// <summary>Binds NetTopologySuite spatial fields in PocketCsvReader.</summary>
public sealed class NetTopologySuiteDelimitedBinding : IReaderBindingProvider
{
    private static readonly DelimitedFieldBinding Binding =
        new(BindField, parsers => parsers.UseNetTopologySuite());
    private static readonly Type[] SupportedGeometryTypes =
    [
        typeof(Geometry), typeof(Point), typeof(LineString), typeof(Polygon),
        typeof(MultiPoint), typeof(MultiLineString), typeof(MultiPolygon), typeof(GeometryCollection)
    ];

    /// <inheritdoc />
    public ReaderFieldBinding? TryBind(ReaderRepresentationRequest request)
    {
        var logicalType = request.Field.LogicalType?.Trim().ToLowerInvariant();
        var supported = logicalType switch
        {
            "geopoint" => request.RuntimeType == typeof(Point),
            "geojson" => request.Format?.Trim().Equals("topojson", StringComparison.OrdinalIgnoreCase) != true
                && SupportedGeometryTypes.Contains(request.RuntimeType),
            "geometry" or "geography" => request.Encoding is SpatialEncoding.Wkt or SpatialEncoding.GeoJson
                && SupportedGeometryTypes.Contains(request.RuntimeType),
            _ => false
        };
        return supported ? new(request.RuntimeType, Binding) : null;
    }

    private static void BindField(ISchemaDescriptorBuilder schema, DataField field, Type type)
    {
        var logicalType = field.LogicalType!.Trim().ToLowerInvariant();
        if (logicalType == "geopoint")
        {
            var format = DelimitedSpatialFields.GetGeoPointFormat(field);
            schema.WithCustomField(type, field.Name, builder => builder.WithDataSourceTypeName(logicalType)
                .WithParser<Point>(text =>
                {
                    var point = GeoPointParser.Parse(text, format);
                    return new Point(point.Longitude, point.Latitude);
                }));
            return;
        }

        var encoding = field.Spatial?.Encoding is SpatialEncoding.Wkt ? "wkt" : "geojson";
        schema.WithCustomField(type, field.Name,
            builder => builder.WithDataSourceTypeName(logicalType).WithFormat(encoding));
    }
}
