using System.Data;
using System.Globalization;
using System.Numerics;
using System.Text;
using Packata.Core.Contracts;

namespace Packata.Http;

/// <summary>Flat, lossless row mappings independent of Arrow. Only declared primitive tabular types are supported.</summary>
internal sealed class RowProjection
{
    internal sealed record Column(DataField Field, string Kind, int Precision, int Scale, int Width, bool Signed,
        string Unit, bool Timezone);
    public Column[] Columns { get; }
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public RowProjection(DataSchema schema, bool avro = false)
    {
        if (schema.Fields.Count == 0) throw new NotSupportedException("At least one field is required.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        Columns = schema.Fields.Select(field =>
        {
            void Reject(string message) => throw new NotSupportedException($"Field '{field.Name}': {message}");
            if (string.IsNullOrWhiteSpace(field.Name) || !names.Add(field.Name)) Reject("Names must be unique and nonempty.");
            if (field.Children.Count > 0 || field.Items is not null || field.Map is not null) Reject("Nested types are unsupported.");
            object? Parameter(string name)
            {
                var matches = field.Constraints.Where(constraint => constraint.Kind == name).ToArray();
                if (matches.Length > 1) Reject($"Duplicate {name} parameter.");
                return matches.SingleOrDefault()?.Value;
            }
            var kind = field.LogicalType?.ToLowerInvariant() ?? "";
            if (kind == "datetime") kind = "timestamp";
            if (kind == "number" && (Parameter("precision") is not null || Parameter("scale") is not null)) kind = "decimal";
            if (kind is not ("integer" or "number" or "decimal" or "boolean" or "string" or "binary" or "date" or "timestamp"))
                Reject($"Unsupported logical type '{kind}'.");
            var precision = 0;
            var scale = 0;
            if (kind == "decimal")
            {
                if (Parameter("precision") is not int p || Parameter("scale") is not int s || p is < 1 or > 38 || s < 0 || s > p)
                    Reject("Decimal requires integer precision 1..38 and scale 0..precision.");
                precision = (int)Parameter("precision")!;
                scale = (int)Parameter("scale")!;
            }
            var width = 64;
            var signed = true;
            if (kind == "integer")
            {
                if ((Parameter("integerWidth") ?? 64) is not int w || w is not (8 or 16 or 32 or 64)
                    || (Parameter("signed") ?? true) is not bool) Reject("Invalid integer width or signedness.");
                width = (int)(Parameter("integerWidth") ?? 64);
                signed = (bool)(Parameter("signed") ?? true);
                if (avro && !signed && width == 64) Reject("Avro has no unsigned 64-bit integer type.");
            }
            var unit = "microsecond";
            var timezone = true;
            if (kind == "timestamp")
            {
                if (Parameter("defaultTimezone") is not null) Reject("Implicit timezone interpretation is unsupported.");
                if ((Parameter("timezone") ?? true) is not bool) Reject("timezone must be a Boolean.");
                timezone = (bool)(Parameter("timezone") ?? true);
                unit = Parameter("precision") as string ?? "microsecond";
                if (Parameter("precision") is not null and not string || unit is not ("second" or "millisecond" or "microsecond" or "nanosecond"))
                    Reject("Unsupported timestamp precision.");
                if (avro && unit == "nanosecond") Reject("Avro output supports at most microsecond precision.");
            }
            return new Column(field, kind, precision, scale, width, signed, unit, timezone);
        }).ToArray();
    }

    public int[] ResolveOrdinals(IDataReader reader) => Columns.Select(column =>
    {
        var name = column.Field.PhysicalName ?? column.Field.Name;
        var matches = Enumerable.Range(0, reader.FieldCount).Where(index => reader.GetName(index) == name).ToArray();
        return matches.Length == 1 ? matches[0] : throw new NotSupportedException($"Field '{column.Field.Name}': source column must resolve uniquely.");
    }).ToArray();

    public object? Normalize(object? value, Column column, int limit)
    {
        try
        {
            if (value is null or DBNull)
                return column.Field.Nullable ? null : throw new InvalidDataException("Null in a non-nullable field.");
            switch (column.Kind)
            {
                case "integer":
                    if (value is not (sbyte or byte or short or ushort or int or uint or long or ulong)) break;
                    var integer = BigInteger.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
                    var minimum = column.Signed ? -(BigInteger.One << (column.Width - 1)) : BigInteger.Zero;
                    var maximum = (BigInteger.One << (column.Signed ? column.Width - 1 : column.Width)) - 1;
                    if (integer < minimum || integer > maximum) throw new OverflowException("Integer exceeds declared width.");
                    return integer;
                case "number" when value is double or float:
                    var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    if (!double.IsFinite(number)) throw new InvalidDataException("Non-finite floating-point values are unsupported.");
                    return number;
                case "decimal" when value is decimal amount:
                    _ = DecimalBytes(amount, column);
                    return amount;
                case "boolean" when value is bool: return value;
                case "string" when value is string text:
                    if (Utf8.GetByteCount(text) > limit) throw new InvalidDataException("String exceeds the buffer limit.");
                    return value;
                case "binary" when value is byte[] bytes:
                    if (bytes.Length > limit) throw new InvalidDataException("Binary exceeds the buffer limit.");
                    return value;
                case "date":
                    if (value is DateOnly day) return day;
                    if (value is DateTime date && date.Kind != DateTimeKind.Local && date.TimeOfDay == TimeSpan.Zero)
                        return DateOnly.FromDateTime(date);
                    break;
                case "timestamp":
                    DateTime instant;
                    if (column.Timezone && value is DateTimeOffset offset) instant = offset.UtcDateTime;
                    else if (column.Timezone && value is DateTime utc && utc.Kind == DateTimeKind.Utc) instant = utc;
                    else if (!column.Timezone && value is DateTime naive && naive.Kind == DateTimeKind.Unspecified) instant = naive;
                    else throw new InvalidDataException("Use UTC DateTime/DateTimeOffset, or Unspecified DateTime for timezone=false.");
                    var ticks = instant.Ticks - DateTime.UnixEpoch.Ticks;
                    var divisor = column.Unit switch { "second" => TimeSpan.TicksPerSecond, "millisecond" => TimeSpan.TicksPerMillisecond,
                        "microsecond" => 10, _ => 1 };
                    if (ticks % divisor != 0) throw new InvalidDataException("Timestamp would lose precision.");
                    if (column.Unit == "nanosecond") _ = checked(ticks * 100);
                    return instant;
            }
            throw new InvalidDataException($"Unsupported CLR type '{value.GetType().Name}'.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new AssetEncodingException(column.Field.Name, column.Kind, exception);
        }
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
