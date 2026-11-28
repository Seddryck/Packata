using System.Globalization;
using System.Text.RegularExpressions;
using Packata.Core.Contracts;

namespace Packata.Core.Execution;

internal enum ConstraintCellState { Missing,
    Invalid,
    Unparsed,
    Value }

internal readonly record struct ConstraintCell(ConstraintCellState State, object? Raw = null,
    QualityValue? Value = null, ScalarValue? Scalar = null);

internal sealed class ConstraintValueParser
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly string _type;
    private readonly string[] _formats;
    private readonly HashSet<string> _missing;
    private readonly HashSet<string> _true;
    private readonly HashSet<string> _false;

    public ConstraintValueParser(DataSchema schema, DataField field)
    {
        _type = field.LogicalType?.ToLowerInvariant() ?? "any";
        var dp = schema.Extensions.TryGetNamespace("datapackage", out _);
        _missing = new(Markers(field.Extensions, "missingValues") ?? Markers(schema.Extensions, "missingValueTokens") ?? Markers(schema.Extensions, "missingValues")
            ?? (dp && _type != "string" ? [""] : []), StringComparer.Ordinal);
        _true = new(Markers(field.Extensions, "trueValues") ?? (dp ? ["true", "True", "TRUE", "1"] : ["true", "True", "TRUE"]), StringComparer.Ordinal);
        _false = new(Markers(field.Extensions, "falseValues") ?? (dp ? ["false", "False", "FALSE", "0"] : ["false", "False", "FALSE"]), StringComparer.Ordinal);
        _formats = Formats(field.Format, dp);
        Supported = (_type is "any" or "string" or "integer" or "number" or "decimal" or "boolean" or "date" or "time" or "timestamp" or "datetime")
            && !_true.Overlaps(_false)
            && (string.IsNullOrEmpty(field.Format) || field.Format is "default" or "any" || _formats.Length > 0);
    }

    public bool Supported { get; }
    public bool HasTextValues => _type is "string" or "any";

    public ConstraintCell Parse(object? raw)
    {
        if (raw is null or DBNull || (raw is string marker && _missing.Contains(marker)))
            return new(ConstraintCellState.Missing);
        if (!Supported) return new(ConstraintCellState.Unparsed, raw);
        var value = ConvertValue(raw);
        return value is null ? new(ConstraintCellState.Invalid, raw) : Valid(raw, value);
    }

    public QualityValue? Bound(object? raw)
    {
        if (_type is "integer" or "number" or "decimal" && ScalarValue.TryCreate(raw, out var scalar)
            && scalar.Kind is ScalarKind.Integer or ScalarKind.Number)
            return QualityValue.From(scalar.Value);
        var value = raw is null ? null : ConvertValue(raw);
        return value is null || value is string or bool ? null : QualityValue.From(value);
    }

    private static ConstraintCell Valid(object raw, object value)
    {
        var domainValue = value is DateOnly or TimeOnly or DateTimeOffset && raw is string ? raw : value;
        return new(ConstraintCellState.Value, raw, QualityValue.From(value),
            ScalarValue.TryCreate(domainValue, out var scalar) ? scalar : null);
    }

    private object? ConvertValue(object raw)
    {
        if (_type is "date" or "time" or "timestamp" or "datetime") return Temporal(raw);
        if (_type == "boolean" && raw is string boolean)
        {
            if (_true.Contains(boolean)) return true;
            if (_false.Contains(boolean)) return false;
            return null;
        }

        if (raw is string text && _type is "integer" or "number" or "decimal") return NumericText(text);
        if (!ScalarValue.TryCreate(raw, out var scalar) || !scalar.IsCompatibleWith(_type)) return null;
        if (_type == "string" && raw is not (string or char)) return null;
        return scalar.Value;
    }

    private object? NumericText(string text)
    {
        if (text.Length == 0 || char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]) || text.Contains('\0')) return null;
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, Invariant, out var integer)) return integer;
        if (_type == "integer") return null;
        if (text.IndexOfAny(['.', 'e', 'E']) < 0) return null;
        return ScalarValue.TryParseNumber(text, out var number) ? number.Value : null;
    }

    private object? Temporal(object raw)
    {
        if (raw is not string text)
        {
            return (_type, raw) switch
            {
                ("date", DateOnly date) => date,
                ("date", DateTime date) when date.TimeOfDay == TimeSpan.Zero => DateOnly.FromDateTime(date),
                ("time", TimeOnly time) => time,
                ("time", TimeSpan time) when time >= TimeSpan.Zero && time < TimeSpan.FromDays(1) => TimeOnly.FromTimeSpan(time),
                ("timestamp" or "datetime", DateTimeOffset timestamp) => timestamp,
                ("timestamp" or "datetime", DateTime date) => new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)),
                _ => null
            };
        }

        const DateTimeStyles exact = DateTimeStyles.None;
        const DateTimeStyles timestampStyle = DateTimeStyles.AssumeUniversal;
        return _type switch
        {
            "date" when DateOnly.TryParseExact(text, _formats, Invariant, exact, out var date) => date,
            "time" when TimeOnly.TryParseExact(text, _formats, Invariant, exact, out var time) => time,
            "timestamp" or "datetime" when DateTimeOffset.TryParseExact(text, _formats, Invariant, timestampStyle, out var timestamp) => timestamp,
            _ => null
        };
    }

    private string[] Formats(string? format, bool dataPackage)
    {
        if (_type is not ("date" or "time" or "timestamp" or "datetime")) return [];
        if (string.IsNullOrEmpty(format) || format is "default" or "any")
        {
            return _type switch
            {
                "date" => ["yyyy-MM-dd"],
                "time" => ["HH:mm:ss", "HH:mm:ss.FFFFFFF"],
                _ => ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"]
            };
        }

        if (!dataPackage || !format.Contains('%')) return [format];
        var directive = new Regex("%.", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var translated = directive.Replace(format, match => match.Value switch
        {
            "%Y" => "yyyy",
            "%m" => "MM",
            "%d" => "dd",
            "%H" => "HH",
            "%M" => "mm",
            "%S" => "ss",
            "%f" => "{fraction}",
            "%z" => "{offset}",
            "%%" => "'[%]'",
            _ => "%"
        });
        if (translated.Replace("'[%]'", "", StringComparison.Ordinal).Contains('%')) return [];
        var fractions = translated.Contains("{fraction}", StringComparison.Ordinal)
            ? Enumerable.Range(1, 6).Select(width => translated.Replace("{fraction}", new string('f', width), StringComparison.Ordinal))
            : [translated];
        return fractions.SelectMany(pattern => pattern.Contains("{offset}", StringComparison.Ordinal)
            ? new[] { pattern.Replace("{offset}", "zzz", StringComparison.Ordinal), pattern.Replace("{offset}", "'Z'", StringComparison.Ordinal) }
            : [pattern]).Select(pattern => pattern.Replace("'[%]'", "'%'", StringComparison.Ordinal)).ToArray();
    }

    private static IEnumerable<string>? Markers(ExtensionMetadata extensions, string name)
        => extensions.TryGetNamespace("datapackage", out var native)
            ? native.GetValueOrDefault(name) as IEnumerable<string> : null;
}
