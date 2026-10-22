namespace Violens.ProxyCore.Http;

/// <summary>
/// Buffers reads from a connection, and passes writes straight through.
/// <see cref="Http1MessageReader"/> reads start lines, headers and chunk
/// sizes a byte at a time; without this each of those bytes was its own
/// read from the socket (or TLS stream) underneath. With it, a whole
/// request head usually arrives in one read and the rest are served from
/// memory. Writes are never held back -- relaying live depends on every
/// write reaching the other side as soon as it's made -- which is why this
/// isn't <see cref="BufferedStream"/>, which buffers both.
///
/// Whatever has been read ahead belongs to the next reader: anything that
/// takes over the connection (a TLS handshake after CONNECT, a WebSocket
/// relay after 101) must read through this stream, never the one beneath.
/// </summary>
internal sealed class ReadBufferedStream(Stream inner, int bufferSize = 16 * 1024) : Stream
{
    private readonly byte[] _buffer = new byte[bufferSize];
    private int _offset;
    private int _count;

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> destination)
    {
        if (destination.Length == 0)
        {
            return 0;
        }

        if (_count == 0)
        {
            // A read at least as big as the buffer gains nothing from it.
            if (destination.Length >= _buffer.Length)
            {
                return inner.Read(destination);
            }

            _offset = 0;
            _count = inner.Read(_buffer, 0, _buffer.Length);
            if (_count == 0)
            {
                return 0;
            }
        }

        return TakeBuffered(destination);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        if (destination.Length == 0)
        {
            return ValueTask.FromResult(0);
        }

        // The common case for a byte-at-a-time reader: served from memory,
        // with no await and no allocation.
        if (_count > 0)
        {
            return ValueTask.FromResult(TakeBuffered(destination.Span));
        }

        return destination.Length >= _buffer.Length
            ? inner.ReadAsync(destination, cancellationToken)
            : FillAndReadAsync(destination, cancellationToken);
    }

    private async ValueTask<int> FillAndReadAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        _offset = 0;
        _count = await inner.ReadAsync(_buffer.AsMemory(), cancellationToken);
        return _count == 0 ? 0 : TakeBuffered(destination.Span);
    }

    private int TakeBuffered(Span<byte> destination)
    {
        var taken = Math.Min(_count, destination.Length);
        _buffer.AsSpan(_offset, taken).CopyTo(destination);
        _offset += taken;
        _count -= taken;
        return taken;
    }

    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> source) => inner.Write(source);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        inner.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default) =>
        inner.WriteAsync(source, cancellationToken);

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return inner.DisposeAsync();
    }
}
