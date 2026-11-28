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
        _missing = new(MissingMarkers(schema, field, dp && _type != "string"), StringComparer.Ordinal);
        (_true, _false) = BooleanMarkers(field, dp);
        _formats = Formats(field.Format, dp);
        Supported = (HasTextValues || IsNumeric || IsTemporal || _type == "boolean")
            && !_true.Overlaps(_false)
            && (UsesDefaultFormat(field.Format) || _formats.Length > 0);
    }

    private bool IsNumeric => _type is "integer" or "number" or "decimal";
    private bool IsTemporal => _type is "date" or "time" or "timestamp" or "datetime";

    private static IEnumerable<string> MissingMarkers(DataSchema schema, DataField field, bool emptyIsMissing)
        => Markers(field.Extensions, "missingValues") ?? Markers(schema.Extensions, "missingValueTokens")
            ?? Markers(schema.Extensions, "missingValues") ?? (emptyIsMissing ? [""] : []);

    private static (HashSet<string> True, HashSet<string> False) BooleanMarkers(DataField field, bool dataPackage)
        => (new(Markers(field.Extensions, "trueValues") ?? (dataPackage ? ["true", "True", "TRUE", "1"] : ["true", "True", "TRUE"]), StringComparer.Ordinal),
            new(Markers(field.Extensions, "falseValues") ?? (dataPackage ? ["false", "False", "FALSE", "0"] : ["false", "False", "FALSE"]), StringComparer.Ordinal));

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
        if (IsNumeric && ScalarValue.TryCreate(raw, out var scalar)
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
        if (IsTemporal) return raw is string temporalText ? TemporalText(temporalText) : NativeTemporal(raw);
        if (raw is string text)
        {
            if (_type == "boolean") return BooleanText(text);
            if (IsNumeric) return NumericText(text);
        }

        return CompatibleScalar(raw);
    }

    private bool? BooleanText(string text)
    {
        if (_true.Contains(text)) return true;
        if (_false.Contains(text)) return false;
        return null;
    }

    private object? CompatibleScalar(object raw)
    {
        if (_type == "string" && raw is not (string or char)) return null;
        return ScalarValue.TryCreate(raw, out var scalar) && scalar.IsCompatibleWith(_type) ? scalar.Value : null;
    }

    private object? NumericText(string text)
    {
        if (text.Length == 0 || char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]) || text.Contains('\0')) return null;
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, Invariant, out var integer)) return integer;
        if (_type == "integer") return null;
        if (text.IndexOfAny(['.', 'e', 'E']) < 0) return null;
        return ScalarValue.TryParseNumber(text, out var number) ? number.Value : null;
    }

    private object? NativeTemporal(object raw) => (_type, raw) switch
    {
        ("date", DateOnly date) => date,
        ("date", DateTime date) when date.TimeOfDay == TimeSpan.Zero => DateOnly.FromDateTime(date),
        ("time", TimeOnly time) => time,
        ("time", TimeSpan time) when time >= TimeSpan.Zero && time < TimeSpan.FromDays(1) => TimeOnly.FromTimeSpan(time),
        ("timestamp" or "datetime", DateTimeOffset timestamp) => timestamp,
        ("timestamp" or "datetime", DateTime date) => new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)),
        _ => null
    };

    private object? TemporalText(string text) => _type switch
    {
        "date" when DateOnly.TryParseExact(text, _formats, Invariant, DateTimeStyles.None, out var date) => date,
        "time" when TimeOnly.TryParseExact(text, _formats, Invariant, DateTimeStyles.None, out var time) => time,
        "timestamp" or "datetime" when DateTimeOffset.TryParseExact(text, _formats, Invariant, DateTimeStyles.AssumeUniversal, out var timestamp) => timestamp,
        _ => null
    };

    private string[] Formats(string? format, bool dataPackage)
    {
        if (!IsTemporal) return [];
        if (UsesDefaultFormat(format)) return DefaultFormats();
        return dataPackage && format!.Contains('%') ? TranslateFormats(format) : [format!];
    }

    private static bool UsesDefaultFormat(string? format) => string.IsNullOrEmpty(format) || format is "default" or "any";

    private string[] DefaultFormats() => _type switch
    {
        "date" => ["yyyy-MM-dd"],
        "time" => ["HH:mm:ss", "HH:mm:ss.FFFFFFF"],
        _ => ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"]
    };

    private static string[] TranslateFormats(string format)
    {
        var directive = new Regex("%.", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var translated = directive.Replace(format, match => TranslateDirective(match.Value));
        if (translated.Replace("'[%]'", "", StringComparison.Ordinal).Contains('%')) return [];
        return ExpandFraction(translated).SelectMany(ExpandOffset)
            .Select(pattern => pattern.Replace("'[%]'", "'%'", StringComparison.Ordinal)).ToArray();
    }

    private static string TranslateDirective(string directive) => directive switch
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
    };

    private static IEnumerable<string> ExpandFraction(string pattern)
        => pattern.Contains("{fraction}", StringComparison.Ordinal)
            ? Enumerable.Range(1, 6).Select(width => pattern.Replace("{fraction}", new string('f', width), StringComparison.Ordinal))
            : [pattern];

    private static string[] ExpandOffset(string pattern)
        => pattern.Contains("{offset}", StringComparison.Ordinal)
            ? [pattern.Replace("{offset}", "zzz", StringComparison.Ordinal), pattern.Replace("{offset}", "'Z'", StringComparison.Ordinal)]
            : [pattern];

    private static IEnumerable<string>? Markers(ExtensionMetadata extensions, string name)
        => extensions.TryGetNamespace("datapackage", out var native)
            ? native.GetValueOrDefault(name) as IEnumerable<string> : null;
}
