using System.Globalization;
using System.Text.Json;
using Packata.Core;
using Packata.Core.Contracts;
using PocketCsvReader;
using PocketCsvReader.Configuration;

namespace Packata.ResourceReaders;

internal static class DelimitedSpatialFields
{
    internal static bool TryAdd(ISchemaDescriptorBuilder schema, DataField field)
    {
        var logicalType = field.LogicalType?.Trim().ToLowerInvariant();
        if (logicalType is not ("geopoint" or "geojson")) return false;

        var resolution = FieldTypeResolver.Resolve(field, TypeMappingOptions.Strict);
        var diagnostic = resolution.Diagnostics.FirstOrDefault(item =>
            item.Severity == TypeMappingSeverity.Error && item.Target == TypeMappingTarget.Source);
        if (diagnostic is not null)
            throw new NotSupportedException($"Cannot bind CSV field '{field.Name}'. {diagnostic.Message}");

        if (resolution.RuntimeType == typeof(GeoPoint))
        {
            ParseSpan<GeoPoint> parser = field.Format?.Trim().ToLowerInvariant() switch
            {
                "array" => ParseArray,
                "object" => ParseObject,
                _ => ParsePair
            };
            schema.WithCustomField(typeof(GeoPoint), field.Name,
                builder => builder.WithDataSourceTypeName(logicalType).WithParser<GeoPoint>(parser));
            return true;
        }

        if (resolution.RuntimeType == typeof(JsonElement))
        {
            schema.WithCustomField(typeof(JsonElement), field.Name,
                builder => builder.WithDataSourceTypeName(logicalType)
                    .WithParser<JsonElement>(text => JsonSerializer.Deserialize<JsonElement>(text)));
            return true;
        }

        throw new NotSupportedException($"Cannot bind CSV field '{field.Name}' as '{logicalType}'.");
    }

    private static GeoPoint ParsePair(ReadOnlySpan<char> text)
    {
        var separator = text.IndexOf(',');
        if (separator >= 0
            && double.TryParse(text[..separator].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            && double.TryParse(text[(separator + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            && double.IsFinite(longitude) && double.IsFinite(latitude))
            return new GeoPoint(longitude, latitude);

        throw new FormatException("Expected a geopoint in longitude, latitude format.");
    }

    private static GeoPoint ParseArray(ReadOnlySpan<char> text)
    {
        var array = JsonSerializer.Deserialize<JsonElement>(text);
        if (array.ValueKind == JsonValueKind.Array && array.GetArrayLength() == 2
            && TryReadCoordinate(array[0], out var longitude)
            && TryReadCoordinate(array[1], out var latitude))
            return new GeoPoint(longitude, latitude);

        throw new FormatException("Expected a geopoint array containing two numbers in longitude, latitude order.");
    }

    private static GeoPoint ParseObject(ReadOnlySpan<char> text)
    {
        var obj = JsonSerializer.Deserialize<JsonElement>(text);
        if (obj.ValueKind == JsonValueKind.Object && obj.EnumerateObject().Count() == 2
            && obj.TryGetProperty("lon", out var lon) && TryReadCoordinate(lon, out var longitude)
            && obj.TryGetProperty("lat", out var lat) && TryReadCoordinate(lat, out var latitude))
            return new GeoPoint(longitude, latitude);

        throw new FormatException("Expected a geopoint object with numeric lon and lat properties.");
    }

    private static bool TryReadCoordinate(JsonElement value, out double coordinate)
    {
        coordinate = default;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out coordinate) && double.IsFinite(coordinate);
    }
}
