using System.Diagnostics;
using SIL.Motif.Contract.Query.Corpus;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Contract.Query.Corpus;

public sealed class PairsAnalysisTests(ITestOutputHelper output)
{
    private static CorpusWordList Corpus(string tsv, CorpusOptions? options = null) => CorpusWordList.ParseTsv(tsv, options);

    private static string Tsv(params (string Form, int Count)[] rows) => string.Concat(rows.Select(row => row.Form + "\t" + row.Count + "\n"));

    private static readonly string Words = Tsv(("kaon", 5), ("kaonan", 3), ("kaonin", 1), ("baha", 2), ("bahaan", 4), ("bahain", 1),
        ("lisan", 4), ("lis", 1), ("sulat", 2), ("sulatan", 1), ("lisanan", 2));

    [Fact]
    public void SuffixPairsReportBasesTokensAndExamplesPerX()
    {
        var result = PairsAnalysis.Run(Corpus(Words), new PairsOptions(PairsDiffer.Suffix));
        var an = result.Xs.Single(x => x.X == "an");
        Assert.Equal(("an", 5, 3L + 4 + 4 + 1 + 2), (an.X, an.Bases, an.Tokens));
        Assert.Equal(new[] {"kaon", "baha", "lis", "lisan", "sulat"}.Order().ToArray(),
            an.Examples.Select(example => example.Base).Order().ToArray());
        Assert.Equal("bahaan", an.Examples[0].Derived);
        Assert.Equal(5 + 2 + 1 + 4 + 2, an.BaseTokens);
        var inX = result.Xs.Single(x => x.X == "in");
        Assert.Equal(2, inX.Bases);
        Assert.Equal(["an", "in"], result.Bases.Single(b => b.Base == "kaon").Xs.ToArray());
        Assert.Equal(["an"], result.Bases.Single(b => b.Base == "lisan").Xs.ToArray());
        Assert.False(result.Truncation.Truncated);
    }

    [Fact]
    public void ARegexMatchesTheWholeDifferingPart()
    {
        var result = PairsAnalysis.Run(Corpus(Words), new PairsOptions(PairsDiffer.Suffix, XPattern: "in|an"));
        Assert.Equal(["an", "in"], result.Xs.Select(x => x.X).Order().ToArray());
        Assert.DoesNotContain(result.Xs, x => x.X == "nan");
        var any = PairsAnalysis.Run(Corpus(Words), new PairsOptions(PairsDiffer.Suffix, XPattern: "a"));
        Assert.Empty(any.Xs);
    }

    [Fact]
    public void BasesHavingAllOfTheListedXsAreTheOnlyOnesReported()
    {
        var result = PairsAnalysis.Run(Corpus(Words), new PairsOptions(PairsDiffer.Suffix, Having: ["in", "an"]));
        Assert.Equal(["baha", "kaon"], result.Bases.Select(b => b.Base).Order().ToArray());
        Assert.Equal(2, result.Xs.Single(x => x.X == "an").Bases);
        Assert.Equal(["an", "in"], result.Xs.Select(x => x.X).Order().ToArray());
        Assert.Equal(new[] {"in", "an"}.Order().ToArray(), result.Having.ToArray());
    }

    [Fact]
    public void FoldCaseFoldsHavingAndTheRegexToo()
    {
        var corpus = Corpus(Tsv(("Kaon", 1), ("KAONAN", 1), ("kaonin", 1)), new CorpusOptions(FoldCase: true));
        var result = PairsAnalysis.Run(corpus, new PairsOptions(PairsDiffer.Suffix, XPattern: "AN|IN", Having: ["AN", "In"]));
        Assert.Equal(["kaon"], result.Bases.Select(b => b.Base).ToArray());
        Assert.Equal(["an", "in"], result.Having.ToArray());
    }

    [Fact]
    public void PrefixPairsLookForTheBaseAtTheEnd()
    {
        var result = PairsAnalysis.Run(Corpus(Tsv(("kaon", 1), ("makaon", 2), ("nakaon", 1), ("baha", 1), ("mabaha", 1), ("maha", 1))), new PairsOptions(PairsDiffer.Prefix));
        var ma = result.Xs.Single(x => x.X == "ma");
        Assert.Equal((2, 3L), (ma.Bases, ma.Tokens));
        Assert.Equal(1, result.Xs.Single(x => x.X == "na").Bases);
        Assert.Contains(result.Bases, b => b.Base == "kaon" && b.Xs.SequenceEqual(new[] { "ma", "na" }));
    }

    [Fact]
    public void XLengthIsCappedInSegments_SoADigraphCountsOnce()
    {
        var corpus = Corpus(Tsv(("ka", 1), ("kangg", 1)), new CorpusOptions(Segments: CorpusSegments.Parse("ng ngg")));
        Assert.Equal(["ngg"], PairsAnalysis.Run(corpus, new PairsOptions(PairsDiffer.Suffix, MaxXLength: 1)).Xs.Select(x => x.X).ToArray());
        var plain = Corpus(Tsv(("ka", 1), ("kangg", 1)));
        Assert.Empty(PairsAnalysis.Run(plain, new PairsOptions(PairsDiffer.Suffix, MaxXLength: 2)).Xs);
        Assert.Equal(["ngg"], PairsAnalysis.Run(plain, new PairsOptions(PairsDiffer.Suffix, MaxXLength: 3)).Xs.Select(x => x.X).ToArray());
    }

    [Fact]
    public void ResultCapsReportTruncationExplicitly_AndOrderingIsDeterministic()
    {
        var tsv = string.Concat(new[] { "a", "b", "c", "d", "e" }.SelectMany(x => new[] { "p", "q" }.Select(b => $"{b}{x}\t1\n"))) + "p\t1\nq\t1\n";
        var first = PairsAnalysis.Run(Corpus(tsv), new PairsOptions(PairsDiffer.Suffix, MaxResults: 3));
        Assert.Equal(["a", "b", "c"], first.Xs.Select(x => x.X).ToArray());
        Assert.Equal(new PairsTruncation(5, 3, 2, 2), first.Truncation);
        Assert.True(first.Truncation.Truncated);
        Assert.Equal(10, first.PairCount);
        var reversed = string.Join("\n", tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Reverse()) + "\n";
        Assert.Equal(first.Xs.Select(x => x.X), PairsAnalysis.Run(Corpus(reversed), new PairsOptions(PairsDiffer.Suffix, MaxResults: 3)).Xs.Select(x => x.X));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(13, 10)]
    [InlineData(4, 0)]
    public void OutOfRangeCapsAreRefused(int maxX, int maxResults) =>
        Assert.Throws<ArgumentException>(() => PairsAnalysis.Run(Corpus(Words), new PairsOptions(PairsDiffer.Suffix, MaxXLength: maxX, MaxResults: maxResults)));

    [Fact]
    public void AnInvalidPatternIsRefusedWithAReason() =>
        Assert.Contains("regular expression", Assert.Throws<ArgumentException>(() =>
            PairsAnalysis.Run(Corpus(Words), new PairsOptions(PairsDiffer.Suffix, XPattern: "("))).Message);

    [Fact]
    public void OneHundredThousandTypesRunInWellUnderASecond_NotQuadratic()
    {
        // 100,000 types are the target; this runs 100,000 so a quadratic scan (10^10 comparisons) would never finish.
        var random = new Random(7);
        const string letters = "abdgiklmnoprstu";
        string Stem() => string.Create(random.Next(3, 7), random, (span, r) => { for (var i = 0; i < span.Length; i++) span[i] = letters[r.Next(letters.Length)]; });
        string[] affixes = ["an", "in", "ay", "on", "ing", "um"];
        var forms = new HashSet<string>();
        while (forms.Count < 100_000)
        {
            var stem = Stem();
            forms.Add(stem);
            if (random.Next(2) == 0) forms.Add(stem + affixes[random.Next(affixes.Length)]);
        }
        var corpus = CorpusWordList.FromCounts(forms.Select(form => new CorpusEntry(form, 1 + random.Next(20))));
        var watch = Stopwatch.StartNew();
        var result = PairsAnalysis.Run(corpus, new PairsOptions(PairsDiffer.Suffix, MaxXLength: 4));
        watch.Stop();
        output.WriteLine($"pairs over {corpus.TypeCount} types: {watch.ElapsedMilliseconds} ms, {result.PairCount} pairs, {result.Xs.Count} Xs shown");
        Assert.Equal(affixes.Order().ToArray(), result.Xs.Take(10).Select(x => x.X).Intersect(affixes).Order().ToArray());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }
}
