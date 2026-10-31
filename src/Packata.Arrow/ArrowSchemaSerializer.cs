using Apache.Arrow;
using Apache.Arrow.Ipc;

namespace Packata.Arrow;

public static class ArrowSchemaSerializer
{
    /// <summary>One encapsulated IPC Schema message. No record batches, EOS marker or file footer.</summary>
    public static async Task<byte[]> SerializeAsync(Schema schema, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        using (var writer = new ArrowStreamWriter(stream, schema, leaveOpen: true))
            await writer.WriteStartAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return stream.ToArray();
    }
}
