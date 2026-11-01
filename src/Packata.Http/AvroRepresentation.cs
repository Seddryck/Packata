using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Packata.Core.Reading;

namespace Packata.Http;

/// <summary>Avro Object Container File encoder with the null codec and bounded blocks.</summary>
public sealed class AvroRepresentation : IAssetRepresentation
{
    public string MediaType => "application/avro";
    public IReadOnlyList<string> Aliases => ["avro/binary"];
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public RepresentationSupport Describe(ServedAsset asset)
    {
        var projection = Project(asset);
        return new(true, "Flat primitives, valid Avro field names, signed integers, decimal bytes, microsecond timestamps; null codec only.",
            WriterSchema(asset, projection));
    }

    public async Task WriteAsync(ServedAsset asset, IDataEndpointReaderFactory readers, Stream destination,
        AssetStreamOptions options, CancellationToken cancellationToken)
    {
        var projection = Project(asset);
        var schema = WriterSchema(asset, projection);
        using var reader = await readers.OpenAsync(asset.Request, cancellationToken).ConfigureAwait(false);
        int[]? ordinals = null;
        using var row = new BoundedBuffer(options.MaxBufferedBytes);
        using var block = new BoundedBuffer(options.MaxBufferedBytes);
        using var prefix = new BoundedBuffer(32);
        var sync = RandomNumberGenerator.GetBytes(16);
        var numberBytes = new byte[8];
        block.Reset();
        block.Write([0x4f, 0x62, 0x6a, 0x01]);
        Long(block, 2);
        Bytes(block, Utf8.GetBytes("avro.schema")); Bytes(block, Utf8.GetBytes(schema));
        Bytes(block, Utf8.GetBytes("avro.codec")); Bytes(block, Utf8.GetBytes("null"));
        Long(block, 0);
        block.Write(sync);
        await destination.WriteAsync(block.GetBuffer().AsMemory(0, (int)block.Length), cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        block.Reset();
        var count = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hasRow = reader.Read();
            cancellationToken.ThrowIfCancellationRequested();
            ordinals ??= !hasRow && reader.FieldCount == 0 ? [] : projection.ResolveOrdinals(reader);
            if (!hasRow) break;
            row.Reset();
            for (var i = 0; i < projection.Columns.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var column = projection.Columns[i];
                var value = projection.Normalize(reader.GetValue(ordinals[i]), column, options.MaxBufferedBytes);
                if (column.Field.Nullable) Long(row, value is null ? 0 : 1);
                if (value is null) continue;
                switch (value)
                {
                    case BigInteger integer: Long(row, (long)integer); break;
                    case decimal amount: Bytes(row, RowProjection.DecimalBytes(amount, column)); break;
                    case double number:
                        BinaryPrimitives.WriteInt64LittleEndian(numberBytes, BitConverter.DoubleToInt64Bits(number));
                        row.Write(numberBytes); break;
                    case bool flag: row.WriteByte(flag ? (byte)1 : (byte)0); break;
                    case string text: Bytes(row, Utf8.GetBytes(text)); break;
                    case byte[] bytes: Bytes(row, bytes); break;
                    case DateOnly day: Long(row, day.DayNumber - DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber); break;
                    case DateTime instant: Long(row, (instant.Ticks - DateTime.UnixEpoch.Ticks) / 10); break;
                }
            }
            if (block.Length + row.Length > options.MaxBufferedBytes) await Flush().ConfigureAwait(false);
            block.Write(row.GetBuffer().AsSpan(0, (int)row.Length));
            if (++count >= options.BatchSize) await Flush().ConfigureAwait(false);
        }
        await Flush().ConfigureAwait(false);

        async Task Flush()
        {
            if (count == 0) return;
            prefix.Reset();
            Long(prefix, count); Long(prefix, block.Length);
            await destination.WriteAsync(prefix.GetBuffer().AsMemory(0, (int)prefix.Length), cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(block.GetBuffer().AsMemory(0, (int)block.Length), cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(sync, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            block.Reset(); count = 0;
        }
    }

    private static RowProjection Project(ServedAsset asset)
    {
        var projection = new RowProjection(asset.Asset.Schema!, avro: true);
        foreach (var column in projection.Columns)
            if (!Regex.IsMatch(column.Field.Name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
                throw new NotSupportedException($"Field '{column.Field.Name}': Avro names must match [A-Za-z_][A-Za-z0-9_]*; names are never silently changed.");
        return projection;
    }

    private static string WriterSchema(ServedAsset asset, RowProjection projection)
    {
        var fields = projection.Columns.Select(column =>
        {
            object type = column.Kind switch
            {
                "integer" => column.Width < 32 || column.Width == 32 && column.Signed ? "int" : "long",
                "number" => "double", "boolean" => "boolean", "string" => "string", "binary" => "bytes",
                "decimal" => new { type = "bytes", logicalType = "decimal", precision = column.Precision, scale = column.Scale },
                "date" => new { type = "int", logicalType = "date" },
                "timestamp" => new { type = "long", logicalType = column.Timezone ? "timestamp-micros" : "local-timestamp-micros" },
                _ => throw new NotSupportedException()
            };
            var field = new Dictionary<string, object?> { ["name"] = column.Field.Name,
                ["type"] = column.Field.Nullable ? new object[] { "null", type } : type };
            if (column.Field.Nullable) field["default"] = null;
            return field;
        });
        return JsonSerializer.Serialize(new { type = "record", name = "Asset_" + asset.Id,
            @namespace = "Packata.Http.Assets", fields });
    }

    private static void Bytes(Stream output, ReadOnlySpan<byte> bytes) { Long(output, bytes.Length); output.Write(bytes); }
    private static void Long(Stream output, long value)
    {
        var encoded = unchecked((ulong)((value << 1) ^ (value >> 63)));
        while (encoded >= 128) { output.WriteByte((byte)((encoded & 127) | 128)); encoded >>= 7; }
        output.WriteByte((byte)encoded);
    }
}
