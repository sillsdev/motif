using System.Text.Json;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class OverviewMeasuresToolTests
{
    private static OverviewResponse Overview(OverviewLookFirst lookFirst, OverviewTextCoverage coverage) =>
        new("Aweti", DateTimeOffset.Parse("2026-09-24T12:00:00Z"), DateTimeOffset.Parse("2026-09-24T11:00:00Z"),
            135, 1, 4, 555, 300, 12, 100, "assessment/1", DateTimeOffset.Parse("2026-09-24T11:30:00Z"),
            120, "sha256:grammar", "sha256:selection", coverage, new OverviewAccuracy(33, 115, 18, 63, 2, 9, 4, 14),
            new OverviewTiming(8, 400, [], 71), Warnings: null)
        {
            SelectionResolved = true,
            LookFirst = lookFirst,
        };

    [Fact]
    public void TheOverviewToolCarriesTheUnparsedWordsAndTheCountsCoverageLeavesOut()
    {
        var unparsed = new OverviewUnparsedWords(3, 12, true,
            [new("kat", 7, UnparsedKind.Refused, "invalidShape"), new("ja", 5, UnparsedKind.NoAnalysis, null)], 12,
            [new(UnparsedKind.Refused, "invalidShape", 1, 7), new(UnparsedKind.NoAnalysis, null, 2, 5)], 2, 1);
        var response = Overview(OverviewLookFirst.Empty with { UnparsedWords = unparsed },
            new OverviewTextCoverage(43, 20, 71, 1, 555, 300) { DigitOnlyOccurrences = 4, OtherWritingSystemOccurrences = 0 });

        using var json = JsonDocument.Parse(ProjectionJson.Serialize(response));

        var words = json.RootElement.GetProperty("lookFirst").GetProperty("unparsedWords");
        Assert.Equal(3, words.GetProperty("words").GetInt32());
        Assert.Equal("kat", words.GetProperty("top")[0].GetProperty("word").GetString());
        Assert.Equal("invalidShape", words.GetProperty("top")[0].GetProperty("code").GetString());
        Assert.Equal(1, words.GetProperty("incorrectSpellingWords").GetInt32());
        Assert.Equal(4, json.RootElement.GetProperty("textCoverage").GetProperty("digitOnlyOccurrences").GetInt32());
    }

    [Fact]
    public void TheOverviewToolCarriesTheFrequencyBandsAndHowManyWordsTheParseDidNotMeasure()
    {
        var response = Overview(OverviewLookFirst.Empty, new OverviewTextCoverage(43, 20, 71, 1, 555, 300) { NotMeasuredWords = 3 }) with
        {
            FrequencyBands = new OverviewFrequencyBands(true, [new(1, 100, 100, 90, 500, 450), new(101, null, 5, 0, 5, 0)]),
        };

        using var json = JsonDocument.Parse(ProjectionJson.Serialize(response));

        var bands = json.RootElement.GetProperty("frequencyBands");
        Assert.True(bands.GetProperty("byText").GetBoolean());
        Assert.False(bands.GetProperty("bands")[1].TryGetProperty("lastRank", out _));
        Assert.Equal(3, json.RootElement.GetProperty("textCoverage").GetProperty("notMeasuredWords").GetInt32());
    }
}
