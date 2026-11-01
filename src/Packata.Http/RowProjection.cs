using System.Data;
using System.Globalization;
using System.Numerics;
using System.Text;
using Packata.Core.Contracts;

namespace Packata.Http;

/// <summary>Flat, lossless row mappings independent of Arrow.</summary>
internal sealed class RowProjection
{
    private const string DecimalKind = "decimal";
    private const string TimestampKind = "timestamp";
    private const string PrecisionParameter = "precision";
    private const string Microsecond = "microsecond";
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    internal sealed record Column(DataField Field, string Kind, int Precision, int Scale, int Width, bool Signed,
        string Unit, bool Timezone);
    public Column[] Columns { get; }

    public RowProjection(DataSchema schema, bool avro = false)
    {
        if (schema.Fields.Count == 0) throw new NotSupportedException("At least one field is required.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in schema.Fields)
            if (string.IsNullOrWhiteSpace(field.Name) || !names.Add(field.Name))
                throw Unsupported(field, "Names must be unique and nonempty.");
        Columns = schema.Fields.Select(field => CreateColumn(field, avro)).ToArray();
    }

    private static Column CreateColumn(DataField field, bool avro)
    {
        if (field.Children.Count > 0 || field.Items is not null || field.Map is not null)
            throw Unsupported(field, "Nested types are unsupported.");
        var kind = Kind(field);
        var (precision, scale) = kind == DecimalKind ? DecimalParameters(field) : (0, 0);
        var (width, signed) = kind == "integer" ? IntegerParameters(field, avro) : (64, true);
        var (unit, timezone) = kind == TimestampKind ? TimestampParameters(field, avro) : (Microsecond, true);
        return new(field, kind, precision, scale, width, signed, unit, timezone);
    }

    private static string Kind(DataField field)
    {
        var kind = field.LogicalType?.ToLowerInvariant() ?? "";
        if (kind == "datetime") kind = TimestampKind;
        if (kind == "number" && (Parameter(field, PrecisionParameter) is not null || Parameter(field, "scale") is not null))
            kind = DecimalKind;
        if (kind is not ("integer" or "number" or DecimalKind or "boolean" or "string" or "binary" or "date" or TimestampKind))
            throw Unsupported(field, $"Unsupported logical type '{kind}'.");
        return kind;
    }

    private static (int, int) DecimalParameters(DataField field)
    {
        if (Parameter(field, PrecisionParameter) is int precision && Parameter(field, "scale") is int scale
            && precision is >= 1 and <= 38 && scale >= 0 && scale <= precision)
            return (precision, scale);
        throw Unsupported(field, "Decimal requires integer precision 1..38 and scale 0..precision.");
    }

    private static (int, bool) IntegerParameters(DataField field, bool avro)
    {
        if ((Parameter(field, "integerWidth") ?? 64) is not int width || width is not (8 or 16 or 32 or 64)
            || (Parameter(field, "signed") ?? true) is not bool signed)
            throw Unsupported(field, "Invalid integer width or signedness.");
        if (avro && !signed && width == 64) throw Unsupported(field, "Avro has no unsigned 64-bit integer type.");
        return (width, signed);
    }

    private static (string, bool) TimestampParameters(DataField field, bool avro)
    {
        if (Parameter(field, "defaultTimezone") is not null) throw Unsupported(field, "Implicit timezone interpretation is unsupported.");
        if ((Parameter(field, "timezone") ?? true) is not bool timezone) throw Unsupported(field, "timezone must be a Boolean.");
        var precision = Parameter(field, PrecisionParameter) ?? Microsecond;
        if (precision is not string unit || unit is not ("second" or "millisecond" or Microsecond or "nanosecond"))
            throw Unsupported(field, "Unsupported timestamp precision.");
        if (avro && unit == "nanosecond") throw Unsupported(field, "Avro output supports at most microsecond precision.");
        return (unit, timezone);
    }

    private static object? Parameter(DataField field, string name)
    {
        var matches = field.Constraints.Where(constraint => constraint.Kind == name).ToArray();
        if (matches.Length > 1) throw Unsupported(field, $"Duplicate {name} parameter.");
        return matches.SingleOrDefault()?.Value;
    }

    public int[] ResolveOrdinals(IDataReader reader) => Columns.Select(column =>
    {
        var name = column.Field.PhysicalName ?? column.Field.Name;
        var matches = Enumerable.Range(0, reader.FieldCount).Where(index => reader.GetName(index) == name).ToArray();
        return matches.Length == 1 ? matches[0] : throw Unsupported(column.Field, "Source column must resolve uniquely.");
    }).ToArray();

    public static object? Normalize(object? value, Column column, int limit)
    {
        try
        {
            if (value is null or DBNull)
                return column.Field.Nullable ? null : throw new InvalidDataException("Null in a non-nullable field.");
            return NormalizeValue(value, column, limit);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new AssetEncodingException(column.Field.Name, column.Kind, exception);
        }
    }

    private static object NormalizeValue(object value, Column column, int limit) => column.Kind switch
    {
        "integer" => NormalizeInteger(value, column),
        "number" when value is double or float => NormalizeNumber(value),
        DecimalKind when value is decimal amount => NormalizeDecimal(amount, column),
        "boolean" when value is bool => value,
        "string" when value is string text => NormalizeString(text, limit),
        "binary" when value is byte[] bytes => NormalizeBinary(bytes, limit),
        "date" => NormalizeDate(value),
        TimestampKind => NormalizeTimestamp(value, column),
        _ => throw new InvalidDataException($"Unsupported CLR type '{value.GetType().Name}'.")
    };

    private static BigInteger NormalizeInteger(object value, Column column)
    {
        if (value is not (sbyte or byte or short or ushort or int or uint or long or ulong))
            throw new InvalidDataException("An integral CLR value is required.");
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? throw new InvalidDataException("An integer is required.");
        var integer = BigInteger.Parse(text, CultureInfo.InvariantCulture);
        var minimum = column.Signed ? -(BigInteger.One << (column.Width - 1)) : BigInteger.Zero;
        var maximum = (BigInteger.One << (column.Signed ? column.Width - 1 : column.Width)) - 1;
        if (integer < minimum || integer > maximum) throw new OverflowException("Integer exceeds declared width.");
        return integer;
    }

    private static double NormalizeNumber(object value)
    {
        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return double.IsFinite(number) ? number : throw new InvalidDataException("Non-finite floating-point values are unsupported.");
    }

    private static decimal NormalizeDecimal(decimal amount, Column column)
    {
        _ = DecimalBytes(amount, column);
        return amount;
    }

    private static string NormalizeString(string text, int limit)
        => Utf8.GetByteCount(text) <= limit ? text : throw new InvalidDataException("String exceeds the buffer limit.");
    private static byte[] NormalizeBinary(byte[] bytes, int limit)
        => bytes.Length <= limit ? bytes : throw new InvalidDataException("Binary exceeds the buffer limit.");
    private static DateOnly NormalizeDate(object value) => value switch
    {
        DateOnly day => day,
        DateTime date when date.Kind != DateTimeKind.Local && date.TimeOfDay == TimeSpan.Zero => DateOnly.FromDateTime(date),
        _ => throw new InvalidDataException("DateOnly or a non-local midnight DateTime is required.")
    };

    private static DateTime ResolveTimestamp(object value, bool timezone) => value switch
    {
        DateTimeOffset offset when timezone => offset.UtcDateTime,
        DateTime utc when timezone && utc.Kind == DateTimeKind.Utc => utc,
        DateTime naive when !timezone && naive.Kind == DateTimeKind.Unspecified => naive,
        _ => throw new InvalidDataException("Use UTC DateTime/DateTimeOffset, or Unspecified DateTime for timezone=false.")
    };

    private static DateTime NormalizeTimestamp(object value, Column column)
    {
        var instant = ResolveTimestamp(value, column.Timezone);
        var ticks = instant.Ticks - DateTime.UnixEpoch.Ticks;
        var divisor = column.Unit switch { "second" => TimeSpan.TicksPerSecond, "millisecond" => TimeSpan.TicksPerMillisecond,
            Microsecond => 10, _ => 1 };
        if (ticks % divisor != 0) throw new InvalidDataException("Timestamp would lose precision.");
        if (column.Unit == "nanosecond") _ = checked(ticks * 100);
        return instant;
    }

    internal static byte[] DecimalBytes(decimal value, Column column)
    {
        var bits = decimal.GetBits(value);
        var unscaled = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
        var scale = (bits[3] >> 16) & 0xff;
        if (scale > column.Scale)
        {
            unscaled = BigInteger.DivRem(unscaled, BigInteger.Pow(10, scale - column.Scale), out var remainder);
            if (remainder != 0) throw new InvalidDataException("Decimal would lose fractional digits.");
        }
        else unscaled *= BigInteger.Pow(10, column.Scale - scale);
        if (unscaled >= BigInteger.Pow(10, column.Precision)) throw new OverflowException("Decimal exceeds precision.");
        if (bits[3] < 0) unscaled = -unscaled;
        return unscaled.ToByteArray(isUnsigned: false, isBigEndian: true);
    }

    private static NotSupportedException Unsupported(DataField field, string message) => new($"Field '{field.Name}': {message}");
}

internal sealed class BoundedBuffer : MemoryStream
{
    private readonly int limit;
    public BoundedBuffer(int limit) : base(new byte[limit], 0, limit, true, true) { this.limit = limit; }
    public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
    public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
    public void Reset() { SetLength(0); Position = 0; }
    private void Check(int count)
    {
        if (Position + count > limit) throw new InvalidDataException("Encoded row or block exceeds MaxBufferedBytes.");
    }
}
