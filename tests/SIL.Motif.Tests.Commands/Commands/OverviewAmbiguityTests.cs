using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Texts;
using Xunit;
using static SIL.Motif.Tests.Commands.StoredWords;

namespace SIL.Motif.Tests.Commands;

public sealed class OverviewAmbiguityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.OverviewAmbiguityTests",
        Guid.NewGuid().ToString("N"));

    public OverviewAmbiguityTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static OverviewAmbiguity? Ambiguity(IReadOnlyDictionary<string, int> places, params AssessedWord[] words) =>
        OverviewAmbiguityBuilder.Build(OverviewMetrics.Build([.. places.Keys.Order(StringComparer.Ordinal)],
            new TextOccurrenceSnapshot(places, new Dictionary<string, int>(), places.Values.Sum(), 0), words).Words, places);

    [Fact]
    public void TheMeanIsOverParsedWordsAndTheAmbiguousWordsAreCounted()
    {
        var places = new Dictionary<string, int> { ["a"] = 1, ["b"] = 1, ["c"] = 1, ["d"] = 1 };

        var ambiguity = Ambiguity(places, Parsed("a", 1), Parsed("b", 2), Parsed("c", 3), Parsed("d", 2))!;

        Assert.Equal(2d, ambiguity.MeanAnalyses);
        Assert.Equal((4, 4, 3), (ambiguity.ParsedWords, ambiguity.CountedWords, ambiguity.AmbiguousWords));
    }

    [Fact]
    public void RefusedStoppedAndUnbuiltWordsAreNotParsedSoTheyAreLeftOut()
    {
        var places = new Dictionary<string, int> { ["ok"] = 1, ["none"] = 1, ["refused"] = 1, ["slow"] = 1, ["bad"] = 1 };

        var ambiguity = Ambiguity(places, Parsed("ok", 2), NoAnalysis("none"), Refused("refused", "tooLong"),
            Stopped("slow", work: true, time: false), InvalidShape("bad"))!;

        Assert.Equal((1, 1, 1, 2d), (ambiguity.ParsedWords, ambiguity.CountedWords, ambiguity.AmbiguousWords, ambiguity.MeanAnalyses));
        Assert.Equal(["ok"], ambiguity.Top.Select(word => word.Word));
    }

    [Fact]
    public void WordsWhoseAnalysesCouldNotAllBeReadAreNotCountedAndTheGapIsVisible()
    {
        var places = new Dictionary<string, int> { ["ok"] = 1, ["partial"] = 1 };
        var partial = Parsed("partial", 1) with
        {
            Morphology = Evidence("partial", 1) with { Unavailable = ["projection failed"] },
        };

        var ambiguity = Ambiguity(places, Parsed("ok", 3), partial)!;

        Assert.Equal((2, 1, 3d), (ambiguity.ParsedWords, ambiguity.CountedWords, ambiguity.MeanAnalyses));
    }

    [Fact]
    public void TheTopTenAreOrderedByAnalysesThenPlacesThenSpellingAndSingleAnalysisWordsAreNotListed()
    {
        var places = Enumerable.Range(0, 14).ToDictionary(index => $"w{index:00}", index => index);
        var words = places.Keys.Select((word, index) => Parsed(word, index < 12 ? 2 + index % 3 : 1)).ToArray();

        var ambiguity = Ambiguity(places, words)!;

        Assert.Equal(12, ambiguity.AmbiguousWords);
        Assert.Equal(OverviewMeasureLimits.TopWords, ambiguity.Top.Count);
        Assert.Equal(ambiguity.Top.OrderByDescending(word => word.Analyses).ThenByDescending(word => word.Occurrences)
            .ThenBy(word => word.Word, StringComparer.Ordinal).ToArray(), ambiguity.Top);
        Assert.Equal(("w11", 4, 11), (ambiguity.Top[0].Word, ambiguity.Top[0].Analyses, ambiguity.Top[0].Occurrences));
        Assert.All(ambiguity.Top, word => Assert.True(word.Analyses > 1));
    }

    [Fact]
    public void WithNoParsedWordThereIsNoAmbiguity()
    {
        Assert.Null(Ambiguity(new Dictionary<string, int> { ["a"] = 1 }, NoAnalysis("a")));
    }

    [Fact]
    public void TheOverviewReadsAmbiguityFromTheStoredParseAndCountsWordsItDidNotMeasure()
    {
        using var fixture = new OverviewMeasuresFixture(_root,
            new Dictionary<string, int> { ["one"] = 5, ["two"] = 3, ["missed"] = 9 });
        fixture.Assess(Parsed("one", 1), Parsed("two", 4));

        var response = OverviewCommand.Overview(new OverviewRequest(fixture.FwDataPath)).Value!;

        Assert.Equal(2.5d, response.Ambiguity!.MeanAnalyses);
        Assert.Equal([("two", 4, 3)], response.Ambiguity.Top.Select(word => (word.Word, word.Analyses, word.Occurrences)));
        Assert.Equal(1, response.TextCoverage.NotMeasuredWords);
    }

    [Fact]
    public void WithoutAStoredRunThereIsNoAmbiguity()
    {
        using var fixture = new OverviewMeasuresFixture(_root, new Dictionary<string, int> { ["one"] = 5 });

        Assert.Null(OverviewCommand.Overview(new OverviewRequest(fixture.FwDataPath)).Value!.Ambiguity);
    }
}
