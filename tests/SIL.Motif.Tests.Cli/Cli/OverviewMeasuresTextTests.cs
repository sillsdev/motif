using System.Text.Json;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class OverviewMeasuresTextTests
{
    private static OverviewResponse Overview(OverviewTextCoverage? coverage = null, OverviewLookFirst? lookFirst = null) =>
        new("Aweti", DateTimeOffset.Parse("2026-09-24T12:00:00Z"), DateTimeOffset.Parse("2026-09-24T11:00:00Z"),
            135, 1, 4, 555, 300, 12, 100, "assessment/1", DateTimeOffset.Parse("2026-09-24T11:30:00Z"),
            120, "sha256:grammar", "sha256:selection",
            coverage ?? new OverviewTextCoverage(43, 20, 71, 1, 555, 300),
            new OverviewAccuracy(33, 115, 18, 63, 2, 9, 4, 14),
            new OverviewTiming(8, 400, [new SlowWordTiming("Akjulule", 1007)], 71),
            Warnings: null)
        {
            SelectionResolved = true,
            LookFirst = lookFirst ?? OverviewLookFirst.Empty,
        };

    private static string Text(OverviewResponse response) =>
        CommandTextRenderer.Render(CommandOutcome<OverviewResponse>.Success(response), asJson: false).Output;

    [Fact]
    public void UnparsedWordsReadAsCountsByRecordedReasonWithTheTopWordsAndTheTotal()
    {
        var unparsed = new OverviewUnparsedWords(148, 1030, true,
            [new("kat", 41, UnparsedKind.NoAnalysis, null), new("ja", 30, UnparsedKind.Refused, "invalidShape")], 71,
            [new(UnparsedKind.NoAnalysis, null, 112, 800), new(UnparsedKind.WorkLimit, null, 30, 180),
                new(UnparsedKind.Refused, "invalidShape", 6, 50)], 3, 4);

        var text = Text(Overview(lookFirst: OverviewLookFirst.Empty with { UnparsedWords = unparsed }));

        Assert.Contains("Unparsed   148 words, 1,030 occurrences: 112 no analysis, 30 stopped at the work limit, " +
            "6 refused (invalidShape)", text, StringComparison.Ordinal);
        Assert.Contains("most occurrences: kat 41, ja 30 (top 2 of 148)", text, StringComparison.Ordinal);
        Assert.Contains("Not listed: 4 words the project marks as an incorrect spelling", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnparsedWordsWithoutPlaceCountsAreListedBySpellingAndNoPlacesAreInvented()
    {
        var unparsed = new OverviewUnparsedWords(2, 0, false,
            [new("ant", 0, UnparsedKind.NoAnalysis, null), new("zed", 0, UnparsedKind.Stopped, null)], 0,
            [new(UnparsedKind.NoAnalysis, null, 1, 0), new(UnparsedKind.Stopped, null, 1, 0)], 2, 0);

        var text = Text(Overview(lookFirst: OverviewLookFirst.Empty with { UnparsedWords = unparsed }));

        Assert.Contains("Unparsed   2 words: 1 no analysis, 1 stopped before finishing", text, StringComparison.Ordinal);
        Assert.Contains("first by spelling: ant, zed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("occurrences:", text.Split('\n').First(line => line.StartsWith("Unparsed", StringComparison.Ordinal)));
    }

    [Fact]
    public void DigitOnlyAndOtherWritingSystemPlacesAreShownApartFromCoverage()
    {
        var coverage = new OverviewTextCoverage(43, 20, 71, 1, 555, 300)
        {
            DigitOnlyOccurrences = 12,
            OtherWritingSystemOccurrences = 1,
        };

        var text = Text(Overview(coverage));

        Assert.Contains("Not in Text Coverage: 12 digit-only occurrences, 1 occurrence in other writing systems", text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Not in Text Coverage", Text(Overview()), StringComparison.Ordinal);
    }

    [Fact]
    public void TheJsonCarriesTheSameFieldsAsTheText()
    {
        var unparsed = new OverviewUnparsedWords(1, 5, true, [new("kat", 5, UnparsedKind.NoAnalysis, null)], 5,
            [new(UnparsedKind.NoAnalysis, null, 1, 5)], 1, 0);
        var response = Overview(new OverviewTextCoverage(43, 20, 71, 1, 555, 300) { DigitOnlyOccurrences = 2, OtherWritingSystemOccurrences = 0 },
            OverviewLookFirst.Empty with { UnparsedWords = unparsed });

        var json = CommandTextRenderer.Render(CommandOutcome<OverviewResponse>.Success(response), asJson: true).Output;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var words = root.GetProperty("lookFirst").GetProperty("unparsedWords");
        Assert.Equal(1, words.GetProperty("words").GetInt32());
        Assert.Equal("kat", words.GetProperty("top")[0].GetProperty("word").GetString());
        Assert.Equal("no-analysis", words.GetProperty("groups")[0].GetProperty("kind").GetString());
        Assert.Equal(2, root.GetProperty("textCoverage").GetProperty("digitOnlyOccurrences").GetInt32());
    }

    private static OverviewFrequencyBands Bands(bool byText) => new(byText,
    [
        new(1, 100, 100, 98, 1500, 1450),
        new(101, 1000, 900, 700, 800, 600),
        new(10001, null, 5, 1, 5, 1),
    ]);

    [Fact]
    public void FrequencyBandsReadAsParsedOverTotalForEachRankRange()
    {
        var text = Text(Overview() with { FrequencyBands = Bands(byText: true) });

        Assert.Contains("Bands      Text Coverage by frequency band", text, StringComparison.Ordinal);
        Assert.Contains("words ranked 1 to 100: 98/100 words parse, 1,450/1,500 occurrences covered", text,
            StringComparison.Ordinal);
        Assert.Contains("words ranked 101 to 1,000: 700/900 words parse", text, StringComparison.Ordinal);
        Assert.Contains("words ranked 10,001 and beyond: 1/5 words parse", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyATextSelectionSaysTextCoverageAndAPartialParseSaysSo()
    {
        var partial = new OverviewTextCoverage(43, 20, 71, 1, 555, 300) { NotMeasuredWords = 7 };

        var text = Text(Overview(partial) with { FrequencyBands = Bands(byText: false) });

        Assert.Contains("Bands      By frequency band", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Text Coverage by frequency band", text, StringComparison.Ordinal);
        Assert.Contains("Partly measured: 7 words of the Selection are not in the stored parse", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Bands", Text(Overview()), StringComparison.Ordinal);
        Assert.DoesNotContain("Partly measured", Text(Overview()), StringComparison.Ordinal);
    }

    [Fact]
    public void TheJsonCarriesTheBandsAndTheNotMeasuredCount()
    {
        var response = Overview(new OverviewTextCoverage(43, 20, 71, 1, 555, 300) { NotMeasuredWords = 2 }) with
        {
            FrequencyBands = Bands(byText: true),
        };

        using var document = JsonDocument.Parse(
            CommandTextRenderer.Render(CommandOutcome<OverviewResponse>.Success(response), asJson: true).Output);

        var bands = document.RootElement.GetProperty("frequencyBands");
        Assert.True(bands.GetProperty("byText").GetBoolean());
        Assert.Equal(3, bands.GetProperty("bands").GetArrayLength());
        Assert.Equal(98, bands.GetProperty("bands")[0].GetProperty("parsedWords").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("textCoverage").GetProperty("notMeasuredWords").GetInt32());
    }
}
