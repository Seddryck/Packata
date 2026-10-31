using System.Data;
using System.Globalization;
using System.Numerics;
using System.Text;
using Apache.Arrow;
using Apache.Arrow.Types;
using Packata.Core.Contracts;

namespace Packata.Arrow;

/// <summary>Converts one synchronous reader result set. Each yielded batch belongs to the caller.</summary>
public static class ArrowRecordBatches
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    /// <summary>Enumeration starts ownership of the reader. Dispose the enumerator on early exit.
    /// Cancellation is checked around synchronous calls; an in-progress Read/GetValue cannot be interrupted.</summary>
    public static IEnumerable<RecordBatch> Read(IDataReader reader, DataSchema schema,
        ArrowBatchOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        options ??= new();
        options.Validate();
        return Iterate(reader, schema, options, cancellationToken);
    }

    private static IEnumerable<RecordBatch> Iterate(IDataReader reader, DataSchema contract,
        ArrowBatchOptions options, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var schema = ArrowSchemaMapper.Map(contract);
            var fields = schema.FieldsList.ToArray();
            var ordinals = ResolveOrdinals(reader, contract);
            var rows = new List<object?[]>();
            long bytes = 4L * fields.Count(f => f.DataType is StringType or BinaryType);
            var baseBytes = bytes;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var hasRow = reader.Read();
                token.ThrowIfCancellationRequested();
                if (!hasRow) break;
                var row = new object?[fields.Length];
                long rowBytes = 0;
                for (var i = 0; i < fields.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var value = reader.GetValue(ordinals[i]);
                        token.ThrowIfCancellationRequested();
                        row[i] = Normalize(value, fields[i]);
                        rowBytes += BufferSize(row[i], fields[i].DataType);
                        if (rowBytes + baseBytes > options.MaxBufferedBytes)
                            throw new InvalidOperationException("A row exceeds MaxBufferedBytes.");
                        // IDataReader providers may reuse binary storage between rows.
                        if (row[i] is byte[] binary) row[i] = binary.ToArray();
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        throw new InvalidDataException($"Cannot convert field '{fields[i].Name}' to {fields[i].DataType.Name}.", error);
                    }
                }
                if (rows.Count > 0 && bytes + rowBytes > options.MaxBufferedBytes)
                {
                    var batch = Build(schema, fields, rows, token);
                    rows.Clear();
                    bytes = baseBytes;
                    yield return batch;
                    token.ThrowIfCancellationRequested();
                }
                rows.Add(row);
                bytes += rowBytes;
                if (rows.Count == options.BatchSize)
                {
                    var batch = Build(schema, fields, rows, token);
                    rows.Clear();
                    bytes = baseBytes;
                    yield return batch;
                }
            }
            if (rows.Count > 0) yield return Build(schema, fields, rows, token);
        }
        finally
        {
            if (!options.LeaveOpen) reader.Dispose();
        }
    }

    private static int[] ResolveOrdinals(IDataReader reader, DataSchema schema)
    {
        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        return schema.Fields.Select(field =>
        {
            var name = field.PhysicalName ?? field.Name;
            var matches = Enumerable.Range(0, names.Length).Where(i => names[i] == name).ToArray();
            if (matches.Length != 1)
                throw ArrowSchemaMapper.Unsupported(field, "Reader must expose exactly one matching physical column name.");
            return matches[0];
        }).ToArray();
    }

    private static object? Normalize(object? value, Field field)
    {
        if (value is null or DBNull)
        {
            if (!field.IsNullable) throw new InvalidOperationException("Null in a non-nullable field.");
            return null;
        }
        switch (field.DataType)
        {
            case IntegerType integer:
                if (value is not (sbyte or byte or short or ushort or int or uint or long or ulong)) break;
                // Convert only integral CLR values; never round decimal/floating input.
                return (integer.BitWidth, integer.IsSigned) switch
                {
                    (8, true) => (object)Convert.ToSByte(value, CultureInfo.InvariantCulture),
                    (16, true) => Convert.ToInt16(value, CultureInfo.InvariantCulture),
                    (32, true) => Convert.ToInt32(value, CultureInfo.InvariantCulture),
                    (64, true) => Convert.ToInt64(value, CultureInfo.InvariantCulture),
                    (8, false) => Convert.ToByte(value, CultureInfo.InvariantCulture),
                    (16, false) => Convert.ToUInt16(value, CultureInfo.InvariantCulture),
                    (32, false) => Convert.ToUInt32(value, CultureInfo.InvariantCulture),
                    (64, false) => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
                    _ => throw new NotSupportedException()
                };
            case DoubleType when value is double or float:
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            case BooleanType when value is bool: return value;
            case StringType when value is string: return value;
            case BinaryType when value is byte[]: return value;
            case Decimal128Type type when value is decimal number:
                ValidateDecimal(number, type);
                return number;
            case Date32Type when value is DateOnly date:
                return date.ToDateTime(TimeOnly.MinValue);
            case Date32Type when value is DateTime date && date.TimeOfDay == TimeSpan.Zero && date.Kind != DateTimeKind.Local:
                return date;
            case TimestampType type:
                var timestamp = value switch
                {
                    DateTimeOffset offset when !string.IsNullOrEmpty(type.Timezone) => offset,
                    DateTime date when date.Kind == DateTimeKind.Utc && !string.IsNullOrEmpty(type.Timezone) => new DateTimeOffset(date),
                    DateTime date when date.Kind == DateTimeKind.Unspecified && string.IsNullOrEmpty(type.Timezone)
                        => new DateTimeOffset(date, TimeSpan.Zero),
                    _ => throw new InvalidOperationException("Timestamp requires UTC DateTime/DateTimeOffset, or Unspecified DateTime for timezone=false.")
                };
                var ticks = timestamp.UtcTicks - DateTime.UnixEpoch.Ticks;
                var divisor = type.Unit switch
                {
                    TimeUnit.Second => TimeSpan.TicksPerSecond, TimeUnit.Millisecond => TimeSpan.TicksPerMillisecond,
                    TimeUnit.Microsecond => 10, _ => 1
                };
                if (ticks % divisor != 0) throw new InvalidOperationException("Timestamp would lose precision.");
                if (type.Unit == TimeUnit.Nanosecond) _ = checked(ticks * 100);
                return timestamp;
        }
        throw new NotSupportedException($"Unsupported CLR value type {value.GetType().Name}.");
    }

    private static void ValidateDecimal(decimal value, Decimal128Type type)
    {
        var bits = decimal.GetBits(value);
        var unscaled = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
        var sourceScale = (bits[3] >> 16) & 0xff;
        if (sourceScale > type.Scale)
        {
            unscaled = BigInteger.DivRem(unscaled, BigInteger.Pow(10, sourceScale - type.Scale), out var remainder);
            if (remainder != 0) throw new InvalidOperationException("Decimal would lose fractional digits.");
        }
        else unscaled *= BigInteger.Pow(10, type.Scale - sourceScale);
        if (unscaled >= BigInteger.Pow(10, type.Precision)) throw new OverflowException("Decimal exceeds declared precision.");
    }

    private static long BufferSize(object? value, IArrowType type) => 1L + (type switch
    {
        StringType => 4L + (value is string text ? Utf8.GetByteCount(text) : 0),
        BinaryType => 4L + (value is byte[] bytes ? bytes.Length : 0),
        Decimal128Type => 16,
        _ => 8
    });

    private static RecordBatch Build(Schema schema, Field[] fields, List<object?[]> rows, CancellationToken token)
    {
        var arrays = new List<IArrowArray>();
        try
        {
            for (var i = 0; i < fields.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                arrays.Add(BuildColumn(fields[i].DataType, rows, i, token));
            }
            token.ThrowIfCancellationRequested();
            return new RecordBatch(schema, arrays, rows.Count);
        }
        catch
        {
            foreach (var array in arrays) array.Dispose();
            throw;
        }
    }

    private static IArrowArray BuildColumn(IArrowType type, List<object?[]> rows, int column, CancellationToken token)
    {
        return type switch
        {
            Int8Type => Fill(new Int8Array.Builder(), (b, v) => b.Append((sbyte)v)),
            Int16Type => Fill(new Int16Array.Builder(), (b, v) => b.Append((short)v)),
            Int32Type => Fill(new Int32Array.Builder(), (b, v) => b.Append((int)v)),
            Int64Type => Fill(new Int64Array.Builder(), (b, v) => b.Append((long)v)),
            UInt8Type => Fill(new UInt8Array.Builder(), (b, v) => b.Append((byte)v)),
            UInt16Type => Fill(new UInt16Array.Builder(), (b, v) => b.Append((ushort)v)),
            UInt32Type => Fill(new UInt32Array.Builder(), (b, v) => b.Append((uint)v)),
            UInt64Type => Fill(new UInt64Array.Builder(), (b, v) => b.Append((ulong)v)),
            DoubleType => Fill(new DoubleArray.Builder(), (b, v) => b.Append((double)v)),
            BooleanType => Fill(new BooleanArray.Builder(), (b, v) => b.Append((bool)v)),
            StringType => Fill(new StringArray.Builder(), (b, v) => b.Append((string)v)),
            BinaryType => Fill(new BinaryArray.Builder(), (b, v) => b.Append((byte[])v)),
            Date32Type => Fill(new Date32Array.Builder(), (b, v) => b.Append((DateTime)v)),
            Decimal128Type decimalType => Fill(new Decimal128Array.Builder(decimalType), (b, v) => b.Append((decimal)v)),
            TimestampType timestampType => Fill(new TimestampArray.Builder(timestampType), (b, v) => b.Append((DateTimeOffset)v)),
            _ => throw new NotSupportedException()
        };

        IArrowArray Fill<TBuilder>(TBuilder builder, Action<TBuilder, object> append)
            where TBuilder : IArrowArrayBuilder<IArrowArray, TBuilder>
        {
            foreach (var row in rows)
            {
                token.ThrowIfCancellationRequested();
                if (row[column] is { } value) append(builder, value);
                else builder.AppendNull();
            }
            return builder.Build(Apache.Arrow.Memory.MemoryAllocator.Default.Value);
        }
    }
}
