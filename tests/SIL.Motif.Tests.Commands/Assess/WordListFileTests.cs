using System.Globalization;
using System.Text;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Help;
using Xunit;

namespace SIL.Motif.Tests.Assess;

public sealed class WordListFileTests : IDisposable
{
    private const int ByteLimit = 8 * 1024 * 1024;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-word-files-" + Guid.NewGuid().ToString("N"));

    public WordListFileTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TheLimitCountsEncodedBytesAndAcceptsExactlyEightMiB(int extra)
    {
        var bytes = new byte[ByteLimit + extra];
        Array.Fill(bytes, (byte)'a');
        var path = Write(bytes);

        var outcome = WordListFile.Resolve(null, path);

        if (extra == 0)
        {
            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            Assert.Equal(ByteLimit, Assert.Single(outcome.Value!).Length);
        }
        else AssertRefusal(outcome, "assess.words-file-too-large", path);
    }

    [Fact]
    public void MultibyteWordsCannotExceedTheEncodedByteLimit()
    {
        var path = Write(Encoding.UTF8.GetBytes(new string('é', ByteLimit / 2 + 1)));

        AssertRefusal(WordListFile.Resolve(null, path), "assess.words-file-too-large", path);
    }

    [Theory]
    [InlineData(new byte[] { 0x61, 0xff, 0x0a })]
    [InlineData(new byte[] { 0x61, 0xc3 })]
    [InlineData(new byte[] { 0xef, 0xbb })]
    [InlineData(new byte[] { 0xc0, 0xaf })]
    [InlineData(new byte[] { 0xed, 0xa0, 0x80 })]
    [InlineData(new byte[] { 0x61, 0x00, 0x62 })]
    [InlineData(new byte[] { 0xff, 0xfe, 0x61, 0x00 })]
    public void InvalidTruncatedOrNulContainingTextIsRefused(byte[] bytes)
    {
        var path = Write(bytes);

        AssertRefusal(WordListFile.Resolve(null, path), "assess.words-file-not-text", path);
    }

    [Fact]
    public void ATruncatedSequenceAtExactlyTheLimitIsNotAnOversizedFile()
    {
        var bytes = new byte[ByteLimit];
        Array.Fill(bytes, (byte)'a');
        bytes[^1] = 0xc3;
        var path = Write(bytes);

        AssertRefusal(WordListFile.Resolve(null, path), "assess.words-file-not-text", path);
    }

    [Fact]
    public void BomMixedLineEndingsUnicodeAndBlankLinesRetainReadLineSemantics()
    {
        var path = Write(Encoding.UTF8.GetBytes("\uFEFF  café  \rkaon\n \t \r\nᜊᜄᜀ\r\nx\uFEFFy\rfin\n"));

        var outcome = WordListFile.Resolve(null, path);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(["café", "kaon", "ᜊᜄᜀ", "x\uFEFFy", "fin"], outcome.Value!);
    }

    [Fact]
    public void OnlyOneInitialUtf8SignatureIsRemoved()
    {
        var path = Write(Encoding.UTF8.GetBytes("\uFEFF\uFEFFalpha\nbeta\uFEFFgamma"));

        Assert.Equal(["\uFEFFalpha", "beta\uFEFFgamma"], WordListFile.Resolve(null, path).Value!);
    }

    [Fact]
    public void InlineOnlyAndNoWordsKeepTheirExistingMeaning()
    {
        IReadOnlyList<string> inline = ["  inline  ", ""];

        Assert.Same(inline, WordListFile.Resolve(inline, null).Value);
        Assert.Same(inline, WordListFile.Resolve(inline, " \t").Value);
        Assert.Empty(WordListFile.Resolve(null, null).Value!);
        Assert.Equal(["file"], WordListFile.Resolve([], Write(Encoding.UTF8.GetBytes("file"))).Value!);
    }

    [Theory]
    [InlineData(false, 0L)]
    [InlineData(true, 0L)]
    [InlineData(true, long.MaxValue)]
    public void ActualReadsStopAfterOneSentinelByteRegardlessOfMetadata(bool seekable, long reportedLength)
    {
        using var stream = new MisreportedStream(ByteLimit + 4096L, seekable, reportedLength);

        var outcome = WordListFile.Read(stream, "changing.txt");

        Assert.Equal(ByteLimit + 1L, stream.Consumed);
        Assert.Equal(0, stream.MetadataReads);
        AssertRefusal(outcome, "assess.words-file-too-large", "changing.txt");
    }

    [Fact]
    public void AnOverstatedLengthDoesNotRefuseSmallReadableText()
    {
        using var stream = new MisreportedStream(2, true, long.MaxValue);

        var outcome = WordListFile.Read(stream, "small.txt");

        Assert.Equal(["aa"], outcome.Value!);
        Assert.Equal(2L, stream.Consumed);
        Assert.Equal(0, stream.MetadataReads);
    }

    [Theory]
    [InlineData("ambiguous", "assess.words-ambiguous", true, "either")]
    [InlineData("empty", "assess.words-file-empty", true, "one word")]
    [InlineData("missing", "assess.words-file-unreadable", true, "read")]
    [InlineData("large", "assess.words-file-too-large", false, "8 MiB")]
    [InlineData("nul", "assess.words-file-not-text", false, "UTF-8")]
    public void EveryWordFileRefusalHasTypedFactsRepeatGuidanceAndHelp(
        string kind, string code, bool shownInWindow, string explanation)
    {
        byte[] bytes = kind switch
        {
            "empty" => Encoding.UTF8.GetBytes("\uFEFF \r\n\t"),
            "large" => Enumerable.Repeat((byte)'a', ByteLimit + 1).ToArray(),
            "nul" => [0x61, 0x00],
            _ => [0x61],
        };
        var path = kind == "missing" ? Path.Combine(_root, "missing.txt") : Write(bytes);
        var outcome = WordListFile.Resolve(kind == "ambiguous" ? ["inline"] : null, path);
        AssertRefusal(outcome, code, path);
        if (kind == "missing")
            Assert.Contains("does not exist", outcome.Refusal!.Message, StringComparison.Ordinal);
        var spec = RefusalCatalog.Specs[code];

        Assert.Equal(FailureReason.InvalidArgument, spec.DefaultReason);
        Assert.NotNull(spec.Next);
        Assert.Equal(NextStep.RepeatCall, spec.Next.Command);
        Assert.Equal(shownInWindow, spec.ShownInWindow);
        var why = HelpCatalog.Load(CultureInfo.InvariantCulture).RefusalWhy(code);
        Assert.NotNull(why);
        Assert.Contains(explanation, why, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("repeat", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnInvalidPathRemainsAnUnreadableFileRefusal()
    {
        const string path = "invalid\0path";

        AssertRefusal(WordListFile.Resolve(null, path), "assess.words-file-unreadable", path);
    }

    private string Write(byte[] bytes)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void AssertRefusal(CommandOutcome<IReadOnlyList<string>> outcome, string code, string path)
    {
        Assert.False(outcome.Succeeded);
        Assert.Equal(code, outcome.Refusal!.Code);
        Assert.Equal(FailureReason.InvalidArgument, outcome.Refusal.Reason);
        Assert.Equal(path, outcome.Refusal.Facts["wordsFile"]);
    }

    private sealed class MisreportedStream(long actualBytes, bool seekable, long reportedLength) : Stream
    {
        public long Consumed { get; private set; }
        public int MetadataReads { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => seekable;
        public override bool CanWrite => false;
        public override long Length { get { MetadataReads++; return reportedLength; } }
        public override long Position
        {
            get { MetadataReads++; return Consumed; }
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, Math.Min(257L, actualBytes - Consumed));
            buffer.AsSpan(offset, read).Fill((byte)'a');
            Consumed += read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
