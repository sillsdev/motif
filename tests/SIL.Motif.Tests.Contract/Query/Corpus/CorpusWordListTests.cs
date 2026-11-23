using System.Text;
using SIL.Motif.Contract.Query.Corpus;
using Xunit;

namespace SIL.Motif.Tests.Contract.Query.Corpus;

public sealed class CorpusWordListTests
{
    private static string Row(string form, int count, string state) => form + "\t" + count + "\t" + state;

    [Fact]
    public void ATsvWithAHeaderNamesItsColumnsAndKeepsTheParseState()
    {
        var corpus = CorpusWordList.ParseTsv(string.Join("\n", "form\ttokens\tstate", Row("kaon", 5, "parsed"), Row("kaonan", 3, "unparsed"), Row("baha", 2, "")));
        Assert.Equal(["baha", "kaon", "kaonan"], corpus.Words.Select(word => word.Form).ToArray());
        Assert.Equal((3, 10L), (corpus.TypeCount, corpus.TokenCount));
        Assert.Equal(["parsed"], corpus.Row(corpus.Words[1]).Get("parse.state"));
        Assert.Empty(corpus.Row(corpus.Words[0]).Get("parse.state"));
    }

    [Fact]
    public void ATsvWithoutAHeaderTakesFormCountState_AndAMissingCountIsOne()
    {
        var corpus = CorpusWordList.ParseTsv("# note\n\nkaon\t4\tparsed\nbaha\n");
        Assert.Equal((1L, 4L), (corpus.Words[0].Tokens, corpus.Words[1].Tokens));
        Assert.Equal("parsed", corpus.Words[1].ParseState);
    }

    [Fact]
    public void ABadCountNamesTheLine()
    {
        var failure = Assert.Throws<CorpusFormatException>(() => CorpusWordList.ParseTsv("kaon\t3\nbaha\tmany\n", source: "words.tsv"));
        Assert.Contains("words.tsv line 2", failure.Message);
    }

    [Fact]
    public void FormsAreNfcAndEqualFormsMerge_AndCaseFoldingIsAnOption()
    {
        var decomposed = "káon";
        var corpus = CorpusWordList.ParseTsv($"{decomposed}\t2\n{decomposed.Normalize(NormalizationForm.FormC)}\t3\nKaon\t1\n");
        Assert.Equal(["Kaon", "káon"], corpus.Words.Select(word => word.Form).ToArray());
        Assert.Equal(5, corpus.Words[1].Tokens);
        var folded = CorpusWordList.ParseTsv("Kaon\t1\nkaon\t4\n", new CorpusOptions(FoldCase: true));
        Assert.Equal([("kaon", 5L)], folded.Words.Select(word => (word.Form, word.Tokens)).ToArray());
    }

    [Fact]
    public void AFolderIsTokenisedOnLettersAndMarks_AndCountsTypesAndTokens()
    {
        var folder = Directory.CreateTempSubdirectory("corpus-test").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "a.txt"), "Kaon, kaon! 12 kaonan-baha; káon\n");
            File.WriteAllText(Path.Combine(folder, "b.txt"), "baha 'baha'");
            File.WriteAllText(Path.Combine(folder, "skip.md"), "ignored");
            var corpus = CorpusWordList.Load(folder);
            Assert.Equal(new[] { ("Kaon", 1L), ("baha", 3L), ("kaon", 1L), ("kaonan", 1L), ("káon", 1L) },
                corpus.Words.Select(word => (word.Form, word.Tokens)).ToArray());
            Assert.Equal(7, corpus.TokenCount);
            Assert.Equal(["kaon", "baha"], CorpusWordList.Tokenize("Kaon baha", foldCase: true).ToArray());
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void RowsCarryTheFormsSourceColumns()
    {
        var corpus = CorpusWordList.ParseTsv("kaonan\t7\n");
        var row = corpus.Row(corpus.Words[0]);
        Assert.Equal(["kaonan"], row.Get("form"));
        Assert.Equal(["7"], row.Get("tokens"));
        Assert.Equal(["CVVCVC"], row.Get("shape"));
        Assert.Equal(["3"], row.Get("syllables"));
        Assert.Equal(["ka"], row.Get("initial:2"));
        Assert.Equal(["an"], row.Get("final:2"));
        Assert.Empty(row.Get("final:8"));
    }

    [Fact]
    public void SegmentationIsApproximateUnlessASegmentFileListsGraphemes_LongestMatchWins()
    {
        var plain = CorpusWordList.ParseTsv("ngana\t1\n");
        Assert.Equal("approximate", plain.SegmentationLabel);
        Assert.Equal(["n", "g", "a", "n", "a"], plain.Row(plain.Words[0]).Segments.ToArray());

        var segments = CorpusSegments.Parse("# digraphs\nng, ngg\tch\n\n");
        var corpus = CorpusWordList.ParseTsv("nggana\t1\nngana\t1\n", new CorpusOptions(Segments: segments));
        Assert.Equal("segment-file", corpus.SegmentationLabel);
        Assert.Equal(["ch", "ng", "ngg"], segments.Graphemes.ToArray());
        Assert.Equal(["ng", "a", "n", "a"], corpus.Row(corpus.Words[0]).Segments.ToArray());
        Assert.Equal(["ngg", "a", "n", "a"], corpus.Row(corpus.Words[1]).Segments.ToArray());
        Assert.Equal(["ngg"], corpus.Row(corpus.Words[1]).Get("initial:1"));
    }
}
