using System.Text;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// The resident parser's reply reader returns a line the moment its terminator arrives, never waiting for more bytes
/// than the reply holds. A pipe that has been drained answers the next read with "nothing yet", not end of file, so a
/// reader that insists on filling its buffer wedges the whole session once a reply ends on a full read.
/// </summary>
public sealed class TerminatedLineReaderTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>A drained pipe: <paramref name="piece"/> bytes per read, then no answer.</summary>
    private sealed class DrainedPipe(byte[] payload, int piece) : Stream
    {
        private int _position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= payload.Length)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return 0;
            }
            var count = Math.Min(Math.Min(piece, buffer.Length), payload.Length - _position);
            payload.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task<string?> ReadOne(byte[] payload, int piece)
    {
        var reader = new TerminatedLineReader(new DrainedPipe(payload, piece), new UTF8Encoding(false));
        var read = reader.ReadLineAsync();
        var finished = await Task.WhenAny(read, Task.Delay(Patience));
        Assert.True(ReferenceEquals(finished, read),
            "The reader kept waiting for more bytes after the reply's newline had arrived.");
        return await read;
    }

    [Theory]
    [InlineData(1024, 1024)]
    [InlineData(8192, 1024)]
    [InlineData(1000, 1024)]
    [InlineData(3000, 7)]
    public async Task AReplyIsReturnedWhenItsNewlineArrivesEvenIfNothingMoreComes(int length, int piece)
    {
        var line = "{\"id\":\"2\",\"text\":\"" + new string('a', length - 20) + "\"}";
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        Assert.Equal(length + 1, bytes.Length);

        Assert.Equal(line, await ReadOne(bytes, piece));
    }

    [Fact]
    public async Task AReplyThatEndsExactlyOnAFullReadIsReturnedWithoutAnotherRead()
    {
        var body = "{\"id\":\"2\",\"text\":\"";
        var tail = "\"}";
        var filler = new string('b', 2048 - body.Length - tail.Length - 1);
        var bytes = Encoding.UTF8.GetBytes(body + filler + tail + "\n");
        Assert.Equal(2048, bytes.Length);

        Assert.Equal(body + filler + tail, await ReadOne(bytes, 1024));
    }

    [Fact]
    public async Task NonAsciiTextSplitAcrossReadsIsDecodedWhole()
    {
        var line = "{\"word\":\"" + string.Concat(Enumerable.Repeat("ñgâ", 700)) + "\"}";
        var bytes = Encoding.UTF8.GetBytes(line + "\n");

        Assert.Equal(line, await ReadOne(bytes, 1023));
    }

    [Fact]
    public async Task TwoRepliesInOneReadAreReturnedOneAtATime()
    {
        var reader = new TerminatedLineReader(new DrainedPipe(Encoding.UTF8.GetBytes("one\ntwo\n"), 1024),
            new UTF8Encoding(false));

        Assert.Equal("one", await reader.ReadLineAsync());
        Assert.Equal("two", await reader.ReadLineAsync());
    }

    [Fact]
    public async Task AnUnterminatedTailIsDiscardedAtEndOfStream()
    {
        var reader = new TerminatedLineReader(new MemoryStream(Encoding.UTF8.GetBytes("whole\npartial")),
            new UTF8Encoding(false));

        Assert.Equal("whole", await reader.ReadLineAsync());
        Assert.Null(await reader.ReadLineAsync());
    }
}
