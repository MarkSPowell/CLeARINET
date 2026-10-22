using System.Text;
using Violens.ProxyCore.Http;
using Xunit;

namespace Violens.ProxyCore.Tests;

public class ReadBufferedStreamTests
{
    [Fact]
    public async Task ARequestHeadIsReadInOneReadFromTheConnection()
    {
        var head = "GET /a HTTP/1.1\r\nHost: example.test\r\nAccept: */*\r\nUser-Agent: test\r\n\r\n";
        var connection = new CountingStream(Encoding.ASCII.GetBytes(head));

        var request = await Http1MessageReader.ReadRequestPreambleAsync(new ReadBufferedStream(connection), CancellationToken.None);

        Assert.Equal("/a", request!.Target);
        Assert.Equal(3, request.Headers.Count);
        // One read for the whole head, where it used to be one per byte.
        Assert.Equal(1, connection.Reads);
    }

    [Fact]
    public async Task WhatWasReadAheadGoesToTheNextReader()
    {
        // The head and the start of the body arrive together; the body
        // reader must still see every byte.
        var connection = new CountingStream(Encoding.ASCII.GetBytes("POST / HTTP/1.1\r\nContent-Length: 11\r\n\r\nhello world"));
        var stream = new ReadBufferedStream(connection, bufferSize: 32);

        var request = await Http1MessageReader.ReadRequestPreambleAsync(stream, CancellationToken.None);
        var body = await Http1MessageReader.ReadBodyAsync(stream, request!.Headers, CancellationToken.None);

        Assert.Equal("hello world", Encoding.ASCII.GetString(body));
    }

    [Fact]
    public async Task WritesGoStraightThrough()
    {
        var connection = new CountingStream([]);
        var stream = new ReadBufferedStream(connection);

        await stream.WriteAsync("abc"u8.ToArray());

        // Without a Flush: relaying live depends on it.
        Assert.Equal("abc", Encoding.ASCII.GetString(connection.Written.ToArray()));
    }

    [Fact]
    public async Task ALargeReadSkipsTheBuffer()
    {
        var data = new byte[100];
        Random.Shared.NextBytes(data);
        var connection = new CountingStream(data);
        var stream = new ReadBufferedStream(connection, bufferSize: 16);

        var read = new byte[100];
        var count = await stream.ReadAsync(read);

        Assert.Equal(100, count);
        Assert.Equal(data, read);
    }

    /// <summary>A readable stream over fixed bytes that counts reads, and records writes.</summary>
    private sealed class CountingStream(byte[] data) : Stream
    {
        private readonly MemoryStream _data = new(data);

        public int Reads { get; private set; }

        public MemoryStream Written { get; } = new();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            Reads++;
            return _data.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(Read(buffer.Span));

        public override void Write(byte[] buffer, int offset, int count) => Written.Write(buffer, offset, count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Written.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
