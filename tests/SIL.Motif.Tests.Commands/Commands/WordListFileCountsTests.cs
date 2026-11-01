using System.Security.Cryptography;
using System.Text;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Commands;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class WordListFileCountsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-word-counts", Guid.NewGuid().ToString("N"));

    public WordListFileCountsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Write(string content, string name = "freq.tsv")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
        return path;
    }

    private static WordListContents Resolved(string path)
    {
        var outcome = WordListFile.ResolveWithCounts(null, path);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!;
    }

    [Fact]
    public void AWordAndATabAndACountGivesTheWordItsCount()
    {
        var contents = Resolved(Write("kaon\t12\nbaga\t3\n"));

        Assert.Equal(["kaon", "baga"], contents.Words);
        Assert.Equal(12, contents.List!.CountOf("kaon"));
        Assert.Equal(3, contents.List.CountOf("baga"));
    }

    [Fact]
    public void AByteOrderMarkAndWindowsLineEndingsReadTheSameAsPlainText()
    {
        var path = Path.Combine(_root, "bom.tsv");
        File.WriteAllBytes(path, [0xef, 0xbb, 0xbf, .. new UTF8Encoding(false).GetBytes("kaon\t12\r\nbaga\r\n")]);

        var contents = Resolved(path);

        Assert.Equal(["kaon", "baga"], contents.Words);
        Assert.Equal(12, contents.List!.CountOf("kaon"));
        Assert.Null(contents.List.CountOf("baga"));
    }

    [Fact]
    public void AnExplicitZeroIsAKnownZeroAndAMissingCountIsNotKnown()
    {
        var contents = Resolved(Write("kaon\t0\nbaga\n"));

        Assert.Equal(0, contents.List!.CountOf("kaon"));
        Assert.Null(contents.List.CountOf("baga"));
    }

    [Fact]
    public void WordsAreKeptInNfdSoAComposedAndADecomposedSpellingAreOneWord()
    {
        var composed = "é";
        var decomposed = "é";
        var contents = Resolved(Write($"{composed}\t4\n{decomposed}\t5\n"));

        var counts = contents.List!.Counts;
        Assert.Equal([decomposed], counts.Keys);
        Assert.Equal(9, counts[decomposed]);
    }

    [Fact]
    public void ACountOnSeveralLinesOfOneWordIsTheirSumAndAPlainLineAddsNothing()
    {
        var contents = Resolved(Write("sita\t2\nsita\t5\nsita\nmutu\t1\n"));

        Assert.Equal(7, contents.List!.CountOf("sita"));
        Assert.Equal(1, contents.List.CountOf("mutu"));
    }

    [Fact]
    public void AMissingCountMeansNotKnownAndIsNeverZero()
    {
        var contents = Resolved(Write("plain\nblank\t\nzero\t0\n"));

        Assert.Equal(["plain", "blank", "zero"], contents.Words);
        Assert.Null(contents.List!.CountOf("plain"));
        Assert.Null(contents.List.CountOf("blank"));
        Assert.Equal(0, contents.List.CountOf("zero"));
        Assert.DoesNotContain("plain", contents.List.Counts.Keys);
    }

    [Fact]
    public void APlainListStillRecordsItsSourceAndHashWithNoCounts()
    {
        var path = Write("one\ntwo\n", "words.txt");

        var list = Resolved(path).List!;

        Assert.Equal("words.txt", list.Source);
        Assert.Empty(list.Counts);
    }

    [Fact]
    public void TheHashIsTheSha256OfTheFilesBytes()
    {
        var path = Write("kaon\t12\n");

        var list = Resolved(path).List!;

        Assert.Equal("sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), list.Sha256);
        Assert.Equal("freq.tsv", list.Source);
    }

    [Fact]
    public void TypedWordsHaveNoSourceHashOrCounts()
    {
        var contents = WordListFile.ResolveWithCounts(["a", "b"], null).Value!;

        Assert.Equal(["a", "b"], contents.Words);
        Assert.Null(contents.List);
    }

    [Theory]
    [InlineData("word\tcount\nkaon\t3\n")]
    [InlineData("kaon\tmany\n")]
    [InlineData("kaon\t-1\n")]
    [InlineData("kaon\t1.5\n")]
    [InlineData("kaon\t3\textra\n")]
    [InlineData("\t3\n")]
    [InlineData("kaon\t2147483648\n")]
    [InlineData("kaon\t2147483647\nkaon\t1\n")]
    public void ALineThatIsNotAWordOrAWordAndACountIsRefusedAndNamesItsLine(string content)
    {
        var outcome = WordListFile.ResolveWithCounts(null, Write(content));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.AssessWordsFileBadRow, outcome.Refusal!.Code);
        Assert.Contains("freq.tsv", outcome.Refusal.Message);
    }

    [Fact]
    public void TheHeaderLineIsRefusedWithItsLineNumber()
    {
        var outcome = WordListFile.ResolveWithCounts(null, Write("word\tcount\nkaon\t3\n"));

        Assert.Contains("Line 1", outcome.Refusal!.Message);
    }

    [Fact]
    public void AFileOfExactlyTheBoundIsReadAndOneByteMoreIsRefused()
    {
        var line = new string('a', 1023) + "\t1\n";
        var atBound = new StringBuilder();
        while (Encoding.UTF8.GetByteCount(atBound.ToString()) + line.Length < WordListFile.MaxBytes) atBound.Append(line);
        var padding = WordListFile.MaxBytes - Encoding.UTF8.GetByteCount(atBound.ToString());
        atBound.Append(new string('\n', padding));
        Assert.Equal(WordListFile.MaxBytes, Encoding.UTF8.GetByteCount(atBound.ToString()));

        var fits = WordListFile.ResolveWithCounts(null, Write(atBound.ToString()));
        var tooBig = WordListFile.ResolveWithCounts(null, Write(atBound + "\n"));

        Assert.True(fits.Succeeded, fits.Refusal?.Message);
        Assert.Equal(RefusalCodes.AssessWordsFileTooLarge, tooBig.Refusal!.Code);
    }

    [Fact]
    public void TheWordsOnlyReadingTakesTheWordColumnSoAssessAndTrialAcceptTheSameFile()
    {
        var outcome = WordListFile.Resolve(null, Write("kaon\t12\nbaga\n"));

        Assert.Equal(["kaon", "baga"], outcome.Value!);
    }
}
