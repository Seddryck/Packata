namespace Packata.Storages;

/// <summary>A seekable input lease. Its Stream retains the original resource identity outside this adapter.</summary>
public sealed class SeekableInput : IDisposable, IAsyncDisposable
{
    private bool _ownsStream;
    public Stream Stream { get; }
    internal SeekableInput(Stream stream, bool ownsStream) { Stream = stream; _ownsStream = ownsStream; }
    internal Stream Release() { _ownsStream = false; return Stream; }
    public void Dispose()
    {
        if (!_ownsStream) return;
        _ownsStream = false;
        Stream.Dispose();
    }
    public async ValueTask DisposeAsync()
    {
        if (!_ownsStream) return;
        _ownsStream = false;
        await Stream.DisposeAsync().ConfigureAwait(false);
    }
}
