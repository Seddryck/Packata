using System.Buffers.Binary;
using System.Text.Json;
using Packata.Arrow;
using Packata.Http;
using Packata.Http.Arrow;

namespace Packata.Cli;

/// <summary>Private packata-pipe-v1 transport: kind byte, uint32 LE length, payload.
/// Kinds: J JSON control, D Arrow bytes, C successful completion, E terminal error.
/// Requests are bounded JSON lines. There is no socket or source inference.</summary>
internal static class PythonHost
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static async Task<int> RunAsync(string path)
    {
        using var output = Console.OpenStandardOutput();
        try
        {
            using var source = await ContractSource.LoadAsync(path, "auto", default, retainArchive: true);
            var contract = source.Map().RequireValue();
            var assets = contract.Assets.Select(asset => new
            {
                id = asset.Id,
                columns = asset.Schema?.Fields.Select(field => new { field.Name, field.LogicalType, field.Nullable, field.Constraints }),
                bindings = asset.EndpointBindings.Count,
                formats = Describe(asset.Schema)
            });
            await Frame(output, (byte)'J', JsonSerializer.SerializeToUtf8Bytes(new { protocol = "packata-pipe-v1", assets }, Json));
            while (await ReadRequest() is { } line)
            {
                using var request = JsonDocument.Parse(line);
                var root = request.RootElement;
                if (root.GetProperty("protocol").GetString() != "packata-pipe-v1") throw new InvalidDataException();
                var operation = root.GetProperty("operation").GetString();
                if (operation == "shutdown") return 0;
                if (operation != "read") throw new InvalidDataException();
                var id = root.GetProperty("asset").GetString() ?? throw new InvalidDataException();
                var binding = root.GetProperty("binding").GetInt32();
                var selected = new ServedAsset(contract, id, binding);
                var streams = source.Archive is null ? null : new PreviewService.ArchiveStreamResolver(source.Archive,
                    source.ArchiveDescriptor ?? throw new InvalidDataException());
                var directory = source.BaseUri.IsFile ? source.BaseUri.LocalPath : string.Empty;
                using var database = selected.Request.Endpoint.Kind == Packata.Core.Contracts.EndpointKind.Database
                    ? new CliDatabase(new CliConfiguration(), directory, contract.Endpoints, [], []) : null;
                var readers = database?.CreateReaders(source.BaseUri) ?? PreviewService.CreateFactory(source.BaseUri, streams: streams);
                await new ArrowIpcExporter(readers).WriteAsync(contract, id, binding, new FrameStream(output),
                    new() { BatchSize = 128, MaxBufferedBytes = 1024 * 1024 });
                await Frame(output, (byte)'C', ReadOnlyMemory<byte>.Empty);
            }
            return 0;
        }
        catch (Exception error)
        {
            await Console.Error.WriteLineAsync($"Python host failed ({error.GetType().Name}).");
            await Frame(output, (byte)'E', JsonSerializer.SerializeToUtf8Bytes(new { code = "HOST_FAILED", message = "Check contract, binding, source prerequisites and declared Arrow types." }, Json));
            return 1;
        }
    }

    private static object[] Describe(Packata.Core.Contracts.DataSchema? schema)
    {
        try
        {
            var mapped = ArrowSchemaMapper.Map(schema ?? throw new NotSupportedException("Asset has no declared schema."));
            return [new { mediaType = "application/vnd.apache.arrow.stream", enabled = true,
                limitation = "Flat Arrow mappings; source drivers and credentials must be configured separately.",
                schema = new { encoding = "arrow-ipc-schema", schemaIpcBase64 = Convert.ToBase64String(
                    ArrowSchemaSerializer.SerializeAsync(mapped).GetAwaiter().GetResult()) } }];
        }
        catch (NotSupportedException error)
        {
            return [new { mediaType = "application/vnd.apache.arrow.stream", enabled = false, limitation = error.Message }];
        }
    }

    private static async Task<string?> ReadRequest()
    {
        var chars = new List<char>();
        var buffer = new char[1];
        while (await Console.In.ReadAsync(buffer.AsMemory()) != 0)
        {
            if (buffer[0] == '\n') return new string(chars.ToArray());
            if (chars.Count >= 65536) throw new InvalidDataException("Request exceeds limit.");
            chars.Add(buffer[0]);
        }
        return null;
    }

    private static async Task Frame(Stream output, byte kind, ReadOnlyMemory<byte> data, CancellationToken token = default)
    {
        var header = new byte[5];
        header[0] = kind;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(1), checked((uint)data.Length));
        await output.WriteAsync(header, token);
        await output.WriteAsync(data, token);
        await output.FlushAsync(token);
    }

    private sealed class FrameStream(Stream output) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (!buffer.IsEmpty)
            {
                var length = Math.Min(buffer.Length, 1024 * 1024);
                await Frame(output, (byte)'D', buffer[..length], cancellationToken);
                buffer = buffer[length..];
            }
        }
        public override Task FlushAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);
        public override void Flush() => output.Flush();
        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, default).GetAwaiter().GetResult();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
