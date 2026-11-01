using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Packata.Core.Reading;

namespace Packata.Http;

public sealed class NdjsonRepresentation : IAssetRepresentation
{
    public string MediaType => "application/x-ndjson";
    public IReadOnlyList<string> Aliases => ["application/ndjson"];
    public RepresentationSupport Describe(ServedAsset asset)
    {
        _ = new RowProjection(asset.Asset.Schema!);
        return new(true, "Flat primitives only. Integers and decimals are exact JSON strings; binary is base64; non-finite numbers are rejected.");
    }

    public async Task WriteAsync(ServedAsset asset, IDataEndpointReaderFactory readers, Stream destination,
        AssetStreamOptions options, CancellationToken cancellationToken)
    {
        var projection = new RowProjection(asset.Asset.Schema!);
        using var reader = await readers.OpenAsync(asset.Request, cancellationToken).ConfigureAwait(false);
        int[]? ordinals = null;
        using var row = new BoundedBuffer(options.MaxBufferedBytes);
        using var block = new BoundedBuffer(options.MaxBufferedBytes);
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
            using (var json = new Utf8JsonWriter(row))
            {
                json.WriteStartObject();
                for (var i = 0; i < projection.Columns.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var column = projection.Columns[i];
                    var value = projection.Normalize(reader.GetValue(ordinals[i]), column, options.MaxBufferedBytes);
                    json.WritePropertyName(column.Field.Name);
                    switch (value)
                    {
                        case null: json.WriteNullValue(); break;
                        case BigInteger integer: json.WriteStringValue(integer.ToString(CultureInfo.InvariantCulture)); break;
                        case decimal amount: json.WriteStringValue(amount.ToString(CultureInfo.InvariantCulture)); break;
                        case double number: json.WriteNumberValue(number); break;
                        case bool flag: json.WriteBooleanValue(flag); break;
                        case string text: json.WriteStringValue(text); break;
                        case byte[] bytes: json.WriteBase64StringValue(bytes); break;
                        case DateOnly day: json.WriteStringValue(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); break;
                        case DateTime instant: json.WriteStringValue(instant.ToString("O", CultureInfo.InvariantCulture)); break;
                    }
                }
                json.WriteEndObject();
            }
            row.WriteByte((byte)'\n');
            if (block.Length + row.Length > options.MaxBufferedBytes) await Flush().ConfigureAwait(false);
            block.Write(row.GetBuffer().AsSpan(0, (int)row.Length));
            if (++count >= options.BatchSize) await Flush().ConfigureAwait(false);
        }
        await Flush().ConfigureAwait(false);

        async Task Flush()
        {
            if (block.Length == 0) return;
            await destination.WriteAsync(block.GetBuffer().AsMemory(0, (int)block.Length), cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            block.Reset();
            count = 0;
        }
    }
}
