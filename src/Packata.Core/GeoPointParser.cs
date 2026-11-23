using System.Globalization;
using System.Text.Json;

namespace Packata.Core;

/// <summary>Parses Data Package geopoint values into longitude and latitude coordinates.</summary>
public static class GeoPointParser
{
    /// <summary>Parses a point using the specified input format.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The format is undefined.</exception>
    /// <exception cref="JsonException">The selected JSON format contains malformed JSON.</exception>
    /// <exception cref="FormatException">The text does not represent a point in the selected format.</exception>
    public static GeoPoint Parse(ReadOnlySpan<char> text, GeoPointFormat format = GeoPointFormat.Default)
    {
        if (format == GeoPointFormat.Auto)
        {
            text = text.TrimStart();
            format = text.IsEmpty ? GeoPointFormat.Default : text[0] switch
            {
                '[' => GeoPointFormat.Array,
                '{' => GeoPointFormat.Object,
                _ => GeoPointFormat.Default
            };
        }

        return format switch
        {
            GeoPointFormat.Default => ParseDefault(text),
            GeoPointFormat.Array => ParseArray(text),
            GeoPointFormat.Object => ParseObject(text),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown geopoint format.")
        };
    }

    /// <summary>Parses a comma-separated longitude and latitude using invariant numeric formatting.</summary>
    /// <exception cref="FormatException">The text does not contain two finite coordinates.</exception>
    public static GeoPoint ParseDefault(ReadOnlySpan<char> text)
    {
        var separator = text.IndexOf(',');
        if (separator >= 0
            && double.TryParse(text[..separator].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            && double.TryParse(text[(separator + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            && double.IsFinite(longitude) && double.IsFinite(latitude))
            return new GeoPoint(longitude, latitude);

        throw new FormatException("Expected a geopoint in longitude, latitude format.");
    }

    /// <summary>Parses a JSON array containing longitude and latitude in that order.</summary>
    /// <exception cref="JsonException">The text is malformed JSON.</exception>
    /// <exception cref="FormatException">The array does not contain exactly two finite numbers.</exception>
    public static GeoPoint ParseArray(ReadOnlySpan<char> text)
    {
        var array = JsonSerializer.Deserialize<JsonElement>(text);
        if (array.ValueKind == JsonValueKind.Array && array.GetArrayLength() == 2
            && TryReadCoordinate(array[0], out var longitude)
            && TryReadCoordinate(array[1], out var latitude))
            return new GeoPoint(longitude, latitude);

        throw new FormatException("Expected a geopoint array containing two numbers in longitude, latitude order.");
    }

    /// <summary>Parses a JSON object containing exactly the numeric <c>lon</c> and <c>lat</c> properties.</summary>
    /// <exception cref="JsonException">The text is malformed JSON.</exception>
    /// <exception cref="FormatException">The object does not contain the two expected finite coordinates.</exception>
    public static GeoPoint ParseObject(ReadOnlySpan<char> text)
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
