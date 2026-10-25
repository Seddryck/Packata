using System.Globalization;
using System.Text.Json;

namespace Packata.Core.Contracts;

public enum ScalarKind { Null, String, Integer, Number, Boolean }

/// <summary>A lossless scalar in Packata's supported CLR range. Integer and number remain distinct.</summary>
public readonly record struct ScalarValue
{
    public ScalarKind Kind { get; }
    public object? Value { get; }
    private ScalarValue(ScalarKind kind, object? value) => (Kind, Value) = (kind, value);

    public static ScalarValue From(object? value)
        => TryCreate(value, out var scalar) ? scalar : throw new ArgumentException("Expected a string, signed 64-bit integer, finite number, boolean, or null.", nameof(value));

    public static bool TryCreate(object? value, out ScalarValue scalar)
    {
        scalar = default;
        switch (value)
        {
            case null: return true;
            case string text: scalar = new(ScalarKind.String, text); return true;
            case char character: scalar = new(ScalarKind.String, character.ToString()); return true;
            case bool boolean: scalar = new(ScalarKind.Boolean, boolean); return true;
            case byte or sbyte or short or ushort or int or uint or long:
                scalar = new(ScalarKind.Integer, Convert.ToInt64(value, CultureInfo.InvariantCulture)); return true;
            case ulong integer when integer <= long.MaxValue: scalar = new(ScalarKind.Integer, (long)integer); return true;
            case decimal number: scalar = new(ScalarKind.Number, number); return true;
            case double number when double.IsFinite(number): scalar = new(ScalarKind.Number, number); return true;
            case float number when float.IsFinite(number): scalar = new(ScalarKind.Number, number); return true;
            case DateTime date: scalar = new(ScalarKind.String, date.ToString("O", CultureInfo.InvariantCulture)); return true;
            case DateTimeOffset date: scalar = new(ScalarKind.String, date.ToString("O", CultureInfo.InvariantCulture)); return true;
            case DateOnly date: scalar = new(ScalarKind.String, date.ToString("O", CultureInfo.InvariantCulture)); return true;
            case TimeOnly time: scalar = new(ScalarKind.String, time.ToString("O", CultureInfo.InvariantCulture)); return true;
            case TimeSpan time: scalar = new(ScalarKind.String, time.ToString("c", CultureInfo.InvariantCulture)); return true;
            case Guid id: scalar = new(ScalarKind.String, id.ToString("D")); return true;
            case JsonElement element:
                return element.ValueKind switch
                {
                    JsonValueKind.Null => true,
                    JsonValueKind.String => TryCreate(element.GetString(), out scalar),
                    JsonValueKind.True or JsonValueKind.False => TryCreate(element.GetBoolean(), out scalar),
                    JsonValueKind.Number => TryJsonNumber(element, out scalar),
                    _ => false
                };
            default: return false;
        }
    }

    private static bool TryJsonNumber(JsonElement element, out ScalarValue scalar)
    {
        scalar = default;
        var text = element.GetRawText();
        if (text.IndexOfAny(['.', 'e', 'E']) < 0)
            return element.TryGetInt64(out var integer) && TryCreate(integer, out scalar);
        return TryParseNumber(text, out scalar);
    }

    /// <summary>Parses a number only when a decimal or finite floating representation retains its numeric text.</summary>
    public static bool TryParseNumber(string text, out ScalarValue scalar)
    {
        scalar = default;
        try
        {
            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && NormalizeNumber(text) == NumberKey(number))
                return TryCreate(number, out scalar);
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating) && double.IsFinite(floating)
                && NormalizeNumber(text) == NumberKey(floating) && TryCreate(floating, out scalar);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException) { return false; }
    }

    public bool IsCompatibleWith(string? logicalType) => logicalType?.ToLowerInvariant() switch
    {
        null or "any" => true,
        "string" => Kind == ScalarKind.String,
        "integer" => Kind == ScalarKind.Integer,
        "number" or "decimal" => Kind is ScalarKind.Integer or ScalarKind.Number,
        "boolean" => Kind == ScalarKind.Boolean,
        "date" or "time" or "datetime" or "timestamp" => Kind == ScalarKind.String,
        _ => false
    };

    public bool Equals(ScalarValue other) => Kind == other.Kind &&
        (Kind == ScalarKind.Number ? NumberKey(Value!).Equals(NumberKey(other.Value!), StringComparison.Ordinal) : Equals(Value, other.Value));
    public override int GetHashCode() => HashCode.Combine(Kind, Kind == ScalarKind.Number ? NumberKey(Value!) : Value);

    // Compare finite number representations without converting a double to decimal or losing precision.
    private static string NumberKey(object value)
    {
        var text = value switch
        {
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            _ => ((decimal)value).ToString(CultureInfo.InvariantCulture)
        };
        return NormalizeNumber(text);
    }

    private static string NormalizeNumber(string text)
    {
        var parts = text.ToLowerInvariant().Split('e');
        var exponent = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        var negative = parts[0].StartsWith('-');
        var mantissa = parts[0].TrimStart('-', '+');
        var point = mantissa.IndexOf('.');
        if (point >= 0) exponent -= mantissa.Length - point - 1;
        var digits = mantissa.Replace(".", "").TrimStart('0');
        if (digits.Length == 0) return "0";
        while (digits.EndsWith('0')) { digits = digits[..^1]; exponent++; }
        return (negative ? "-" : "") + digits + "e" + exponent.ToString(CultureInfo.InvariantCulture);
    }
}
