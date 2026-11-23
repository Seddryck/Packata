namespace Packata.Core.Contracts;

public enum SpatialModel
{
    Unspecified,
    Geometry,
    Geography
}

public enum SpatialSubtype
{
    Point,
    LineString,
    Polygon,
    MultiPoint,
    MultiLineString,
    MultiPolygon,
    GeometryCollection
}

public enum SpatialEncoding
{
    Wkt,
    Wkb,
    GeoJson,
    TopoJson,
    Ewkt,
    Ewkb
}

public enum SpatialContent
{
    Geometry,
    GeoJsonValue,
    Topology
}

/// <summary>Declared spatial meaning and encoding, independent of the CLR value and physical storage type.</summary>
public sealed record SpatialDescriptor(
    SpatialModel Model = SpatialModel.Unspecified,
    SpatialSubtype? SubType = null,
    string? Crs = null,
    int? Dimensions = null,
    SpatialEncoding? Encoding = null,
    SpatialContent Content = SpatialContent.Geometry,
    string? Algorithm = null,
    string? Orientation = null,
    double? Epoch = null)
{
    private IReadOnlyList<double>? boundingBox;

    public IReadOnlyList<double>? BoundingBox
    {
        get => boundingBox;
        init => boundingBox = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}

/// <summary>Source identity qualifying a native physical type. Custom-property attribution is preserved separately.</summary>
public sealed record PhysicalTypeContext(string? Provider = null, string? Dialect = null, string? Server = null);
