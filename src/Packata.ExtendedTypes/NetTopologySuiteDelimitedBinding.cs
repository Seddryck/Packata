using NetTopologySuite.Geometries;
using Packata.Core;
using Packata.Core.Contracts;
using Packata.ResourceReaders;
using PocketCsvReader.Configuration;
using PocketCsvReader.NetTopologySuite;

namespace Packata.ExtendedTypes;

internal static class NetTopologySuiteDelimitedBinding
{
    internal static DelimitedRepresentationBinding Binding { get; } =
        new(parsers => parsers.UseNetTopologySuite(), BindField);

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
