using System.Collections;
using System.Data;
using System.Globalization;

namespace Packata.Core.Contracts;

/// <summary>A descriptor-authored SQL literal, independent of inferred database default text.</summary>
public sealed record ColumnDefaultLiteral(DbType Type, object Value);

internal static class ColumnDefaultDeclarations
{
    private const NumberStyles Integer = NumberStyles.AllowLeadingSign;
    private const NumberStyles Decimal = Integer | NumberStyles.AllowDecimalPoint;
    private const NumberStyles Floating = Decimal | NumberStyles.AllowExponent;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] TimeFormats = TemporalFormats("HH:mm:ss");
    private static readonly string[] DateTimeFormats = TemporalFormats("yyyy-MM-dd'T'HH:mm:ss");
    private static readonly string[] OffsetFormats = DateTimeFormats.Select(format => format + "zzz").ToArray();

    internal static ColumnDefaultLiteral? Read(IEnumerable<(string? Name, object? Value)> properties,
        string path, ICollection<MappingDiagnostic> diagnostics)
    {
        var declarations = properties.Where(property => property.Name == "defaultValue").ToArray();
        if (declarations.Length == 0) return null;
        if (declarations.Length > 1)
        {
            diagnostics.Add(new("DEFAULT001", MappingSeverity.Error, path, $"Field '{path}' must declare defaultValue only once."));
            return null;
        }

        if (declarations[0].Value is IDictionary { Count: 2 } map
            && map["type"] is string type && map["value"] is string value)
        {
            var literal = Parse(type, value);
            if (literal is not null) return literal;
            diagnostics.Add(new("DEFAULT001", MappingSeverity.Error, path,
                $"Invalid defaultValue for field '{path}' with type '{type}'. Use a supported type and a value within its lexical form and range."));
            return null;
        }

        diagnostics.Add(new("DEFAULT001", MappingSeverity.Error, path,
            $"Field '{path}' defaultValue requires an object containing exactly a string 'type' and a string 'value'."));
        return null;
    }

    private static ColumnDefaultLiteral? Parse(string type, string value) => type switch
    {
        "string" => new(DbType.String, value),
        "boolean" when value is "true" or "false" => new(DbType.Boolean, value == "true"),
        "int16" when short.TryParse(value, Integer, Invariant, out var number) => new(DbType.Int16, number),
        "int32" when int.TryParse(value, Integer, Invariant, out var number) => new(DbType.Int32, number),
        "int64" when long.TryParse(value, Integer, Invariant, out var number) => new(DbType.Int64, number),
        "decimal" when TryDecimal(value, out var number) => new(DbType.Decimal, number),
        "single" when float.TryParse(value, Floating, Invariant, out var number) && float.IsFinite(number)
            => new(DbType.Single, number),
        "double" when double.TryParse(value, Floating, Invariant, out var number) && double.IsFinite(number)
            => new(DbType.Double, number),
        "date" when DateOnly.TryParseExact(value, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var date)
            => new(DbType.Date, date),
        "time" when TimeOnly.TryParseExact(value, TimeFormats, Invariant, DateTimeStyles.None, out var time)
            => new(DbType.Time, time),
        "datetime" when DateTime.TryParseExact(value, DateTimeFormats, Invariant, DateTimeStyles.None, out var stamp)
            => new(DbType.DateTime, stamp),
        "datetime-offset" when DateTimeOffset.TryParseExact(
            value.EndsWith('Z') ? value[..^1] + "+00:00" : value, OffsetFormats, Invariant, DateTimeStyles.None, out var stamp)
            => new(DbType.DateTimeOffset, stamp),
        _ => null
    };

    private static string[] TemporalFormats(string prefix)
        => [prefix, .. Enumerable.Range(1, 6).Select(count => prefix + "." + new string('f', count))];

    private static bool TryDecimal(string value, out decimal number)
    {
        // decimal.TryParse can round a valid-looking input to fit its 96-bit coefficient.
        return decimal.TryParse(value, Decimal, Invariant, out number)
            && NormalizeDecimal(value) == NormalizeDecimal(number.ToString("0.############################", Invariant));
    }

    private static string NormalizeDecimal(string value)
    {
        var parts = value.TrimStart('+', '-').Split('.');
        var whole = parts[0].TrimStart('0');
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : string.Empty;
        var sign = value.StartsWith('-') && (whole.Length > 0 || fraction.Length > 0) ? "-" : string.Empty;
        return $"{sign}{whole}.{fraction}";
    }
}
