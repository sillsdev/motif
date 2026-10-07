using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class WordDispositionTests
{
    private static readonly ParseAnalysis Book = Reading("form-1", "msa-1");
    private static readonly ParseAnalysis Child = Reading("form-2", "msa-2");

    [Fact]
    public void AnApprovedStoredAnalysisShowsAPaleAWithTilesForEveryOtherDisposition()
    {
        var disposition = WordDisposition.From(
            AnalysisMarkingState.Create(Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("same", Book)));

        Assert.Equal("A", disposition.MarkLetter);
        Assert.False(disposition.IsChanged);
        var row = Assert.Single(disposition.StoredRows);
        Assert.Equal(["A", "U", "D", "–"], row.Tiles.Select(tile => tile.Letter));
        Assert.True(row.Tiles[0].IsCurrent);
        Assert.Null(row.Tiles[0].Choice);
        Assert.Equal(AnalysisMarkingActionKind.MakeUnknown, row.Tiles[1].Choice?.Kind);
        Assert.Equal(AnalysisMarkingActionKind.Disapprove, row.Tiles[2].Choice?.Kind);
        Assert.Equal(AnalysisMarkingActionKind.RemoveAnalysis, row.Tiles[3].Choice?.Kind);
        Assert.All(row.Tiles, tile => Assert.True(tile.IsAvailable));
        Assert.False(disposition.HasReadings);
    }

    [Fact]
    public void AStagedOpinionMovesTheMarkAndMakesItSolid()
    {
        var marking = AnalysisMarkingState.Create(Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("same", Book))
            .WithStagedTransitions([new StagedMarkingTransition("Approved", "Unknown", "stored-1")]);

        var disposition = WordDisposition.From(marking);

        Assert.Equal("U", disposition.MarkLetter);
        Assert.True(disposition.IsChanged);
        Assert.True(disposition.StoredRows[0].Tiles[1].IsStaged);
        Assert.StartsWith("Changed: FieldWorks will hold it as Unknown", disposition.MarkTip);
    }

    [Fact]
    public void ParserOnlyReadingsAreNumberedAndOfferApprovedAndUnknown()
    {
        var disposition = WordDisposition.From(
            AnalysisMarkingState.Create(Token(Stored(Book, ReadingGrade.Disapproved, "stored-1")), Result("different", Child)));

        Assert.True(disposition.HasReadings);
        Assert.Equal("+1", disposition.AddLabel);
        var reading = Assert.Single(disposition.Readings);
        Assert.Equal(1, reading.Number);
        Assert.Equal([WordDispositionKind.Approved, WordDispositionKind.Unknown], reading.Tiles.Select(tile => tile.Kind));
        Assert.All(reading.Tiles, tile => Assert.NotNull(tile.Choice));
    }

    [Fact]
    public void AChosenReadingShowsACheckWithItsNumberInTheOpinionItIsAddedAs()
    {
        var marking = AnalysisMarkingState.Create(Token(), Result("new", Child))
            .WithStagedTransitions([new StagedMarkingTransition("Not in FieldWorks", "Unknown", ReadingIndex: 0)]);

        var disposition = WordDisposition.From(marking);

        Assert.Equal("✓1", disposition.AddLabel);
        Assert.Equal(WordDispositionKind.Unknown, disposition.ChosenKind);
        Assert.Equal("–", disposition.MarkLetter);
        Assert.False(disposition.IsChanged);
    }

    [Fact]
    public void AStagedIncorrectSpellingMakesTheMarkSolid()
    {
        var marking = AnalysisMarkingState.Create(Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("same", Book))
            .WithStagedTransitions([new StagedMarkingTransition("Current spelling", "Incorrect")]);

        Assert.True(WordDisposition.From(marking).IsChanged);
    }

    private static ParseAnalysis Reading(string form, string msa) =>
        new([new ParseMorph(form, msa, null, null)]);

    private static ProjectAnalysis Stored(ParseAnalysis reading, string opinion, string id) =>
        new(ProjectAnalysisKey.For(reading),
            [new ParserReadingMorph("entry", "gloss", "n", null, false, "silfw://entry") { Entry = "entry" }])
        {
            StoredAnalysisId = id,
            StoredAnalysisOpinion = opinion,
            Identity = new ApprovedMorphology(reading.Morphs.Select(morph => new ApprovedMorph(
                morph.Form, morph.Msa, morph.InflType, ["entry"])).ToArray())
            {
                SourceAnalysisId = id,
                SourceWordformGuid = "wordform-1",
            },
        };

    private static TextToken Token(params ProjectAnalysis[] analyses) =>
        new("book", "book", null, null)
        {
            StoredAnalyses = analyses,
            StoredAnalysisId = analyses.FirstOrDefault()?.StoredAnalysisId,
            WordformId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011"),
            OccurrenceIndex = 0,
        };

    private static AssessmentWordResult Result(string word, params ParseAnalysis[] readings) =>
        new(word, readings.Length == 0 ? "no-analysis" : "analysed", false, "Complete", 3, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, word, 3, false, false, false, readings, []),
            Readings = readings.Select(_ => new ParserReading(
                [new ParserReadingMorph("entry", "gloss", "n", null, false, "silfw://entry")])).ToArray(),
        };
}
