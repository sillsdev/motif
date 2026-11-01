using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using Xunit;
using static SIL.Motif.Tests.Commands.StoredWords;

namespace SIL.Motif.Tests.Commands;

public sealed class OverviewUnparsedWordsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.OverviewUnparsedWordsTests",
        Guid.NewGuid().ToString("N"));

    public OverviewUnparsedWordsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static OverviewUnparsedWords Unparsed(IReadOnlyDictionary<string, int> places, params AssessedWord[] words) =>
        OverviewUnparsedBuilder.Build(OverviewMetrics.Build([.. places.Keys.Order(StringComparer.Ordinal)],
            new SIL.Motif.Host.Texts.TextOccurrenceSnapshot(places, new Dictionary<string, int>(), places.Values.Sum(), 0),
            words).Words, places);

    [Fact]
    public void UnparsedWordsAreGroupedOnlyByWhatTheParserRecorded()
    {
        var places = new Dictionary<string, int>
        {
            ["a"] = 1, ["b"] = 1, ["c"] = 1, ["d"] = 1, ["e"] = 1, ["f"] = 1, ["g"] = 1, ["h"] = 1, ["ok"] = 5,
        };

        var unparsed = Unparsed(places, NoAnalysis("a"), NoAnalysis("b"), Refused("c", "tooLong"),
            InvalidShape("d"), Stopped("e", work: true, time: false), Stopped("f", work: false, time: true),
            Stopped("g", work: true, time: true), Parsed("ok", 1), NoAnalysis("h"));

        Assert.Equal(8, unparsed.Words);
        Assert.Equal(
            [("no-analysis", null, 3), ("both", null, 1), ("refused", "invalidShape", 1), ("refused", "tooLong", 1),
                ("time-safety-net", null, 1), ("work-limit", null, 1)],
            unparsed.Groups.Select(group => (group.Kind, group.Code, group.Words)));
        Assert.Equal(unparsed.Groups.Count, unparsed.GroupCount);
    }

    [Fact]
    public void ThereIsNoInferredReasonSoAPlainNoParseIsOneGroup()
    {
        var places = new Dictionary<string, int> { ["x"] = 3, ["y"] = 2 };

        var unparsed = Unparsed(places, NoAnalysis("x"), NoAnalysis("y"));

        var group = Assert.Single(unparsed.Groups);
        Assert.Equal((UnparsedKind.NoAnalysis, (string?)null, 2, 5), (group.Kind, group.Code, group.Words, group.Occurrences));
    }

    [Fact]
    public void WordsAreRankedByPlacesAndTiesBreakBySpelling()
    {
        var places = new Dictionary<string, int> { ["beta"] = 4, ["alpha"] = 4, ["gamma"] = 9, ["delta"] = 1 };

        var unparsed = Unparsed(places, NoAnalysis("beta"), NoAnalysis("alpha"), NoAnalysis("gamma"), NoAnalysis("delta"));

        Assert.True(unparsed.RankedByOccurrences);
        Assert.Equal([("gamma", 9), ("alpha", 4), ("beta", 4), ("delta", 1)],
            unparsed.Top.Select(word => (word.Word, word.Occurrences)));
        Assert.Equal(18, unparsed.Occurrences);
    }

    [Fact]
    public void TheListIsTheTopTenWithTheTotalBesideIt()
    {
        var places = Enumerable.Range(0, 25).ToDictionary(index => $"w{index:00}", index => index + 1);

        var unparsed = Unparsed(places, [.. places.Keys.Select(word => NoAnalysis(word))]);

        Assert.Equal(25, unparsed.Words);
        Assert.Equal(OverviewMeasureLimits.TopWords, unparsed.Top.Count);
        Assert.Equal("w24", unparsed.Top[0].Word);
        Assert.Equal(unparsed.Top.Sum(word => word.Occurrences), unparsed.TopOccurrences);
        Assert.Equal(places.Values.Sum(), unparsed.Occurrences);
    }

    [Fact]
    public void ASelectionWithNoPlaceCountsListsBySpellingAndSaysSo()
    {
        var places = new Dictionary<string, int> { ["zulu"] = 0, ["alfa"] = 0, ["mike"] = 0 };

        var unparsed = Unparsed(places, NoAnalysis("zulu"), NoAnalysis("alfa"), Parsed("mike", 1));

        Assert.False(unparsed.RankedByOccurrences);
        Assert.Equal(["alfa", "zulu"], unparsed.Top.Select(word => word.Word));
        Assert.Equal(0, unparsed.Occurrences);
    }

    [Fact]
    public void WordsTheProjectMarksAsIncorrectSpellingsAreCountedApartAndNotListed()
    {
        var places = new Dictionary<string, int> { ["typo"] = 50, ["real"] = 2, ["fine"] = 1 };

        var unparsed = Unparsed(places, NoAnalysis("typo", ProjectStanding.IncorrectSpelling), NoAnalysis("real"),
            Parsed("fine", 1));

        Assert.Equal(1, unparsed.Words);
        Assert.Equal(["real"], unparsed.Top.Select(word => word.Word));
        Assert.Equal(1, unparsed.IncorrectSpellingWords);
        Assert.Equal(2, unparsed.Occurrences);
    }

    [Fact]
    public void WordsTheRunDidNotMeasureAreNotCalledUnparsed()
    {
        var places = new Dictionary<string, int> { ["seen"] = 1, ["missed"] = 7 };

        var unparsed = Unparsed(places, NoAnalysis("seen"));

        Assert.Equal(["seen"], unparsed.Top.Select(word => word.Word));
    }

    [Fact]
    public void InvalidShapeWordsStayInTheDenominatorOfTextCoverage()
    {
        using var fixture = new OverviewMeasuresFixture(_root,
            new Dictionary<string, int> { ["ok"] = 6, ["odd"] = 4 });
        fixture.Assess(Parsed("ok", 1), InvalidShape("odd"));

        var overview = OverviewCommand.Overview(new OverviewRequest(fixture.FwDataPath));

        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        var response = overview.Value!;
        Assert.Equal(2, response.SelectionWordCount);
        Assert.Equal(10, response.TextCoverage.TotalOccurrences);
        Assert.Equal((1, 1), (response.TextCoverage.ParsedWords, response.TextCoverage.SkippedWords));
        Assert.Equal(50d, response.WordCoveragePercent);
        Assert.Equal(60d, response.TextCoverage.OccurrenceCoveragePercent);
        var refused = Assert.Single(response.LookFirst.UnparsedWords!.Groups);
        Assert.Equal((UnparsedKind.Refused, "invalidShape"), (refused.Kind, refused.Code));
    }

    [Fact]
    public void TheOverviewRanksUnparsedWordsByPlacesInTheChosenTextsAndCountsWhatCoverageLeavesOut()
    {
        using var fixture = new OverviewMeasuresFixture(_root,
            new Dictionary<string, int> { ["rare"] = 1, ["common"] = 30, ["middle"] = 8, ["ok"] = 4 },
            digitOnlyPlaces: 12, otherWritingSystemPlaces: 5);
        fixture.Assess(NoAnalysis("rare"), NoAnalysis("common"), Stopped("middle", work: true, time: false), Parsed("ok", 1));

        var response = OverviewCommand.Overview(new OverviewRequest(fixture.FwDataPath)).Value!;

        var unparsed = response.LookFirst.UnparsedWords!;
        Assert.Equal(["common", "middle", "rare"], unparsed.Top.Select(word => word.Word));
        Assert.Equal((12, 5), (response.TextCoverage.DigitOnlyOccurrences, response.TextCoverage.OtherWritingSystemOccurrences));
        Assert.Equal(43, response.TextCoverage.TotalOccurrences);
        Assert.True(unparsed.ByText);
    }

    [Fact]
    public void ASelectionWithNoTextsHasNoLeftOutCountsAndNoPlaceRanking()
    {
        using var fixture = new OverviewMeasuresFixture(_root, new Dictionary<string, int> { ["ignored"] = 3 },
            digitOnlyPlaces: 9, addedWords: ["zed", "ant"], chooseText: false);
        fixture.Assess(NoAnalysis("zed"), NoAnalysis("ant"));

        var response = OverviewCommand.Overview(new OverviewRequest(fixture.FwDataPath)).Value!;

        Assert.Null(response.TextCoverage.DigitOnlyOccurrences);
        Assert.Null(response.TextCoverage.OtherWritingSystemOccurrences);
        var unparsed = response.LookFirst.UnparsedWords!;
        Assert.False(unparsed.RankedByOccurrences);
        Assert.False(unparsed.ByText);
        Assert.Equal(["ant", "zed"], unparsed.Top.Select(word => word.Word));
    }

    [Fact]
    public void WithoutAStoredRunThereIsNoUnparsedRowAtAll()
    {
        using var fixture = new OverviewMeasuresFixture(_root, new Dictionary<string, int> { ["ok"] = 1 });

        var response = OverviewCommand.Overview(new OverviewRequest(fixture.FwDataPath)).Value!;

        Assert.Null(response.AssessmentId);
        Assert.Null(response.LookFirst.UnparsedWords);
    }
}
