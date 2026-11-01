using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Texts;
using Xunit;
using static SIL.Motif.Tests.Commands.StoredWords;

namespace SIL.Motif.Tests.Commands;

public sealed class OverviewFrequencyBandsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.OverviewFrequencyBandsTests",
        Guid.NewGuid().ToString("N"));

    public OverviewFrequencyBandsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static (OverviewFrequencyBands? Bands, OverviewTextCoverage Coverage) Bands(
        IReadOnlyDictionary<string, int> places, params AssessedWord[] words)
    {
        var metrics = OverviewMetrics.Build([.. places.Keys.Order(StringComparer.Ordinal)],
            new TextOccurrenceSnapshot(places, new Dictionary<string, int>(), places.Values.Sum(), 0), words);
        return (OverviewFrequencyBandBuilder.Build(metrics.Words, places, byText: true), metrics.TextCoverage);
    }

    private static Dictionary<string, int> Descending(int count) =>
        Enumerable.Range(0, count).ToDictionary(index => $"w{index:00000}", index => count - index);

    [Fact]
    public void BandsHoldEveryWordOnceSoTheyAddUpToTheSelectionTotalsAndTheCoverageShare()
    {
        var places = Descending(12_000);
        var words = places.Keys.Select((word, index) => index % 3 == 0 ? NoAnalysis(word) : Parsed(word, 1)).ToArray();

        var (bands, coverage) = Bands(places, words);

        Assert.Equal([(1, 100), (101, 1_000), (1_001, 10_000), (10_001, (int?)null)],
            bands!.Bands.Select(band => (band.FirstRank, band.LastRank)).ToArray());
        Assert.Equal([100, 900, 9_000, 2_000], bands.Bands.Select(band => band.Words));
        Assert.Equal(12_000, bands.Bands.Sum(band => band.Words));
        Assert.Equal(coverage.ParsedWords, bands.Bands.Sum(band => band.ParsedWords));
        Assert.Equal(coverage.TotalOccurrences, bands.Bands.Sum(band => band.Occurrences));
        Assert.Equal(coverage.ParsedOccurrences, bands.Bands.Sum(band => band.ParsedOccurrences));
    }

    [Fact]
    public void TheMostUsedHundredWordsAreTheFirstBand()
    {
        var places = Descending(300);

        var (bands, _) = Bands(places, [.. places.Keys.Select(word => Parsed(word, 1))]);

        var first = bands!.Bands[0];
        Assert.Equal(Enumerable.Range(201, 100).Sum(), first.Occurrences);
    }

    [Fact]
    public void WordsTiedAtARankEdgeAreSplitBySpellingSoTheBandsAreTheSameEveryRun()
    {
        var places = Enumerable.Range(0, 250).ToDictionary(index => $"w{index:000}", _ => 1);
        var words = places.Keys.Select(word => string.CompareOrdinal(word, "w100") < 0 ? NoAnalysis(word) : Parsed(word, 1)).ToArray();

        var (bands, _) = Bands(places, words);
        var (again, _) = Bands(places.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value), words);

        Assert.Equal(bands!.Bands, again!.Bands);
        Assert.Equal((0, 100), (bands.Bands[0].ParsedWords, bands.Bands[0].Words));
        Assert.Equal((150, 150), (bands.Bands[1].ParsedWords, bands.Bands[1].Words));
    }

    [Fact]
    public void ASelectionSmallerThanTheFirstBandHasOnlyThatBandWithItsRealSize()
    {
        var places = new Dictionary<string, int> { ["a"] = 3, ["b"] = 2, ["c"] = 1 };

        var (bands, _) = Bands(places, Parsed("a", 1), NoAnalysis("b"), Parsed("c", 1));

        var band = Assert.Single(bands!.Bands);
        Assert.Equal((1, (int?)100, 3, 2, 6, 4), (band.FirstRank, band.LastRank, band.Words, band.ParsedWords,
            band.Occurrences, band.ParsedOccurrences));
    }

    [Fact]
    public void ASelectionWithNoPlaceCountsHasNoBands()
    {
        var places = new Dictionary<string, int> { ["a"] = 0, ["b"] = 0 };

        var (bands, _) = Bands(places, Parsed("a", 1));

        Assert.Null(bands);
    }

    [Fact]
    public void OnlyASelectionOfChosenTextsIsLabelledTextCoverage()
    {
        var places = new Dictionary<string, int> { ["a"] = 3, ["b"] = 2 };
        var metrics = OverviewMetrics.Build(["a", "b"], new TextOccurrenceSnapshot(places,
            new Dictionary<string, int>(), 5, 0), [Parsed("a", 1), Parsed("b", 1)]);

        Assert.True(OverviewFrequencyBandBuilder.Build(metrics.Words, places, byText: true)!.ByText);
        Assert.False(OverviewFrequencyBandBuilder.Build(metrics.Words, places, byText: false)!.ByText);
    }

    [Fact]
    public void WordsTheRunDidNotMeasureAreInTheirBandAsNotParsedAndAreCounted()
    {
        using var fixture = new OverviewMeasuresFixture(_root,
            new Dictionary<string, int> { ["ok"] = 6, ["odd"] = 4, ["missed"] = 3 });
        fixture.Assess(Parsed("ok", 1), InvalidShape("odd"));

        var response = OverviewCommand.Overview(new OverviewRequest(fixture.FwDataPath)).Value!;

        var band = Assert.Single(response.FrequencyBands!.Bands);
        Assert.True(response.FrequencyBands.ByText);
        Assert.Equal((3, 1, 13, 6), (band.Words, band.ParsedWords, band.Occurrences, band.ParsedOccurrences));
        Assert.Equal(1, response.TextCoverage.NotMeasuredWords);
    }

    [Fact]
    public void AWordListWithNoPlaceCountsAndAProjectWithNoStoredRunHaveNoBands()
    {
        using var words = new OverviewMeasuresFixture(_root, new Dictionary<string, int> { ["ignored"] = 3 },
            addedWords: ["zed", "ant"], chooseText: false);
        words.Assess(Parsed("zed", 1), NoAnalysis("ant"));
        using var unparsed = new OverviewMeasuresFixture(_root, new Dictionary<string, int> { ["ok"] = 1 });

        var wordList = OverviewCommand.Overview(new OverviewRequest(words.FwDataPath)).Value!;
        var none = OverviewCommand.Overview(new OverviewRequest(unparsed.FwDataPath)).Value!;

        Assert.Null(wordList.FrequencyBands);
        Assert.Null(none.FrequencyBands);
    }

    [Fact]
    public void AWordListSelectionIsRankedByItsOwnCountsAndNeverCalledTextCoverage()
    {
        var list = new SelectionWordList("freq.tsv", "sha256:" + new string('b', 64),
            new Dictionary<string, int> { ["zed"] = 9, ["ant"] = 2 });
        using var fixture = new OverviewMeasuresFixture(_root, new Dictionary<string, int> { ["ignored"] = 3 },
            addedWords: ["zed", "ant", "mid"], chooseText: false, wordList: list);
        fixture.Assess(Parsed("zed", 1), NoAnalysis("ant"), NoAnalysis("mid"));

        var response = OverviewCommand.Overview(new OverviewRequest(fixture.FwDataPath)).Value!;

        var bands = response.FrequencyBands!;
        Assert.False(bands.ByText);
        Assert.Equal(1, bands.NoCountWords);
        var band = Assert.Single(bands.Bands);
        Assert.Equal((3, 1, 11, 9), (band.Words, band.ParsedWords, band.Occurrences, band.ParsedOccurrences));
        Assert.Equal(0, response.TextCoverage.TotalOccurrences);
        var unparsed = response.LookFirst.UnparsedWords!;
        Assert.True(unparsed.RankedByOccurrences);
        Assert.False(unparsed.ByText);
        Assert.Equal([("ant", (int?)2), ("mid", null)], unparsed.Top.Select(word => (word.Word, word.Occurrences)));
    }

    [Fact]
    public void AnExplicitZeroInAWordListShowsAsZeroAndRanksBeforeWordsWithNoCount()
    {
        var list = new SelectionWordList("freq.tsv", "sha256:" + new string('b', 64),
            new Dictionary<string, int> { ["kay"] = 5, ["zed"] = 0 });
        using var fixture = new OverviewMeasuresFixture(_root, new Dictionary<string, int> { ["ignored"] = 3 },
            addedWords: ["kay", "zed", "ant", "bee"], chooseText: false, wordList: list);
        fixture.Assess(Parsed("kay", 1), NoAnalysis("zed"), NoAnalysis("ant"), NoAnalysis("bee"));

        var unparsed = OverviewCommand.Overview(new OverviewRequest(fixture.FwDataPath)).Value!.LookFirst.UnparsedWords!;

        Assert.Equal([("zed", (int?)0), ("ant", null), ("bee", null)], unparsed.Top.Select(word => (word.Word, word.Occurrences)));
        Assert.Equal(2, unparsed.NoCountWords);
        Assert.Equal(0, unparsed.Occurrences);
    }

    [Fact]
    public void NoWordsAreMissingACountWhenTheSelectionHasNoCountsOrEveryWordHasOne()
    {
        var list = new SelectionWordList("freq.tsv", "sha256:" + new string('b', 64),
            new Dictionary<string, int> { ["ant"] = 2, ["bee"] = 1 });
        using var counted = new OverviewMeasuresFixture(_root, new Dictionary<string, int> { ["ignored"] = 3 },
            addedWords: ["ant", "bee"], chooseText: false, wordList: list);
        counted.Assess(NoAnalysis("ant"), NoAnalysis("bee"));
        using var typed = new OverviewMeasuresFixture(_root, new Dictionary<string, int> { ["ignored"] = 3 },
            addedWords: ["ant", "bee"], chooseText: false);
        typed.Assess(NoAnalysis("ant"), NoAnalysis("bee"));

        Assert.Equal(0, OverviewCommand.Overview(new OverviewRequest(counted.FwDataPath)).Value!.LookFirst.UnparsedWords!.NoCountWords);
        Assert.Equal(0, OverviewCommand.Overview(new OverviewRequest(typed.FwDataPath)).Value!.LookFirst.UnparsedWords!.NoCountWords);
    }

    [Fact]
    public void ADefaultSelectionOfChosenTextsIgnoresAWordListsCounts()
    {
        var list = new SelectionWordList("freq.tsv", "sha256:" + new string('b', 64),
            new Dictionary<string, int> { ["zed"] = 900 });
        using var fixture = new OverviewMeasuresFixture(_root, new Dictionary<string, int> { ["one"] = 4, ["two"] = 1 },
            addedWords: ["zed"], wordList: list);
        fixture.Assess(Parsed("one", 1), Parsed("two", 1), NoAnalysis("zed"));

        var response = OverviewCommand.Overview(new OverviewRequest(fixture.FwDataPath)).Value!;

        Assert.True(response.FrequencyBands!.ByText);
        Assert.Equal(0, response.FrequencyBands.NoCountWords);
        Assert.Equal(5, response.FrequencyBands.Bands.Sum(band => band.Occurrences));
    }
}
