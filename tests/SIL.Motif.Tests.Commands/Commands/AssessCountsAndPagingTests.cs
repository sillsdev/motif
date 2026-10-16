using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Trait("MotifTestLevel", "Unit")]
public sealed class AssessCountsAndPagingTests
{
    private static AssessmentWordResult Word(string word, string outcome, params string?[][] readings) =>
        new(word, outcome, outcome is "timed-out" or "capped", "Search completed", 1, null)
        {
            Morphology = new ParseWordEvidence("s", 0, word, 1, false, false, false,
                readings.Select(reading => new ParseAnalysis(reading.Select(guess =>
                    new ParseMorph("form", null, null, guess)).ToArray())).ToArray(), []),
        };

    private static readonly string?[] Identified = [null, null];
    private static readonly string?[] WithGuess = [null, "?baga"];

    private static AssessmentWordResult[] Fixture() =>
    [
        Word("kaon", "analysed", Identified),
        Word("a+baga", "analysed", WithGuess),
        Word("mixed", "analysed", WithGuess, Identified),
        Word("zzz", "no-analysis"),
        Word("slow", "timed-out"),
        Word("big", "capped"),
        Word("bad", "skipped"),
    ];

    [Fact]
    public void CountsSplitOutcomesAndSeparateAcceptedFromGuessOnly()
    {
        var counts = AssessmentCounts.Of(Fixture());

        Assert.Equal(new AssessmentCounts(7, 3, 1, 1, 1, 1, Accepted: 2, GuessOnly: 1), counts);
    }

    [Fact]
    public void TheResponseCarriesCountsInItsJson()
    {
        var response = new AssessCommandResponse(null!, null!, [], "") { Words = Fixture() };

        var json = JsonNode.Parse(ProjectionJson.Serialize(response))!;

        Assert.Equal(2, json["counts"]!["accepted"]!.GetValue<int>());
        Assert.Equal(1, json["counts"]!["guessOnly"]!.GetValue<int>());
    }

    private static AssessCommandResponse Big(int count) => new(null!, null!, ["a-1"], "")
    {
        Words = Enumerable.Range(0, count).Select(index => Word("w" + index, index % 3 == 0 ? "no-analysis"
            : "analysed", index % 2 == 0 ? WithGuess : Identified)).ToArray(),
        Measurements = [new ProducedAssessmentReference("a-1", "ParseTime", "inv")],
    };

    [Fact]
    public void AConcisePageSaysShowingXOfNAndHowToReadTheRest()
    {
        var page = AssessSummary.Page(Big(97), offset: 0, limit: 25, detailed: false);

        Assert.Equal("showing 1-25 of 97", page["showing"]!.GetValue<string>());
        Assert.Equal(25, page["words"]!.AsArray().Count);
        Assert.Equal(25, page["nextOffset"]!.GetValue<int>());
        Assert.Equal("a-1", page["assessment"]!.GetValue<string>());
        Assert.Equal(97, page["counts"]!["words"]!.GetValue<int>());
    }

    [Fact]
    public void ALaterPageStartsWhereTheOffsetSaysAndTheLastPageHasNoNext()
    {
        var page = AssessSummary.Page(Big(97), offset: 75, limit: 25, detailed: false);

        Assert.Equal("showing 76-97 of 97", page["showing"]!.GetValue<string>());
        Assert.Equal("w75", page["words"]![0]!["word"]!.GetValue<string>());
        Assert.Null(page["nextOffset"]);
    }

    [Fact]
    public void AConcisePageStaysSmallForAThousandWords()
    {
        var page = AssessSummary.Page(Big(1000), offset: 0, limit: 25, detailed: false);

        Assert.True(page.ToJsonString().Length < 8 * 1024, page.ToJsonString().Length.ToString());
    }

    [Fact]
    public void AConciseRowNamesTheOutcomeAndWhetherTheReadingWasAcceptedOrGuessed()
    {
        var rows = AssessSummary.Page(new AssessCommandResponse(null!, null!, [], "") { Words = Fixture() },
            0, 25, false)["words"]!.AsArray();

        Assert.Equal("accepted", rows[0]!["reading"]!.GetValue<string>());
        Assert.Equal("guess-only", rows[1]!["reading"]!.GetValue<string>());
        Assert.Equal("no-analysis", rows[3]!["outcome"]!.GetValue<string>());
        Assert.Null(rows[3]!["reading"]);
    }

    [Fact]
    public void ADetailedPageAddsReadingsForTheRowsOfThatPageOnly()
    {
        var page = AssessSummary.Page(Big(100), offset: 0, limit: 5, detailed: true);

        var rows = page["words"]!.AsArray();
        Assert.Equal(5, rows.Count);
        Assert.All(rows, row => Assert.NotNull(row!["morphology"]));
    }

    private static string Temp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "motif-words-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void AWordsFileHoldsOneWordPerLineAndBlankLinesAreDropped()
    {
        var path = Temp("kaon\n\n  baga  \r\nlakaw\n");
        try
        {
            var outcome = WordListFile.Resolve(null, path);

            Assert.True(outcome.Succeeded);
            Assert.Equal(["kaon", "baga", "lakaw"], outcome.Value!);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WordsAloneOrNeitherPassThrough()
    {
        Assert.Equal(["a"], WordListFile.Resolve(["a"], null).Value!);
        Assert.Empty(WordListFile.Resolve(null, null).Value!);
    }

    [Fact]
    public void ARefusalNamesAMissingEmptyOrDoubledWordList()
    {
        var empty = Temp("\n  \n");
        try
        {
            Assert.Equal("assess.words-file-unreadable",
                WordListFile.Resolve(null, Path.Combine(Path.GetTempPath(), "motif-nope.txt")).Refusal!.Code);
            Assert.Equal("assess.words-file-empty", WordListFile.Resolve(null, empty).Refusal!.Code);
            Assert.Equal("assess.words-ambiguous", WordListFile.Resolve(["a"], empty).Refusal!.Code);
        }
        finally { File.Delete(empty); }
    }
}
