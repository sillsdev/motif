using System.Linq;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins the one projection every page's word row reads: what FieldWorks holds, what PanGloss built, the differing
/// morphemes, what the two mean together, and where the word occurs.
/// </summary>
public sealed class WordRowProjectionTests
{
    private static ParserReadingMorph Morph(string form, string gloss, string? id) =>
        new(form, gloss, "v", null, false, null)
        {
            AllomorphId = id is null ? null : "form-" + id,
            GrammaticalInfoId = id is null ? null : "msa-" + id,
        };

    private static readonly ParserReadingMorph A = Morph("a-", "3SG", "a");
    private static readonly ParserReadingMorph Li = Morph("li-", "PST", "li");
    private static readonly ParserReadingMorph Kul = Morph("kul", "cut", "kul");
    private static readonly ParserReadingMorph Ku = Morph("ku-", "INF", "ku");
    private static readonly ParserReadingMorph L = Morph("l", "eat", "l");
    private static readonly ParserReadingMorph Fv = Morph("-a", "FV", "fv");

    private static ParserReading Stored(string opinion, params ParserReadingMorph[] morphs) =>
        new(morphs) { StoredAnalysisId = "analysis-" + string.Concat(morphs.Select(morph => morph.Form)), StoredAnalysisOpinion = opinion };

    private static AssessmentWordResult Alikula(params ParserReading[] readings) =>
        new("alikula", "analysed", false, "Search completed", 12, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            Readings = readings,
            ReadingGrades = readings.Select(_ => ReadingGrade.NoOpinion).ToArray(),
            Morphology = new ParseWordEvidence("v1", 0, "alikula", 12, false, false, false,
                readings.Select(_ => new ParseAnalysis([new ParseMorph("x", "y", null, null)])).ToArray(), []),
            ExpectedAnalysis = Stored(ReadingGrade.Approved, A, Li, Kul, Fv),
            StoredAnalyses = [Stored(ReadingGrade.Approved, A, Li, Kul, Fv)],
            OccurrenceCount = 3,
            TryWordLink = "silfw://localhost/link?tool=Analyses",
        };

    [Fact]
    public void ForAlikula_FieldWorksKulAgainstPanGlossKuAndL_MarksPositionsThreeAndFour()
    {
        var row = WordRowProjection.Of(Alikula(new ParserReading([A, Li, Ku, L, Fv])), CompareColumnKind.NoMatch);

        Assert.Equal(WordRowOutcome.Different, row.Outcome);
        Assert.Equal(["a-", "li-", "kul", "-a"], row.FieldWorksMorphemes.Select(morph => morph.Form));
        Assert.Equal(["a-", "li-", "ku-", "l", "-a"], row.PanGlossMorphemes.Select(morph => morph.Form));
        Assert.Equal([3, 4], row.DifferingPositions);
        Assert.Equal(("Built something else", WordRowTone.Problem), (row.Meaning, row.Tone));
    }

    [Fact]
    public void TheRowCarriesTheWordsFormGlossOpinionPlacesTimeAndWordAnalysesLink()
    {
        var row = WordRowProjection.Of(Alikula(new ParserReading([A, Li, Ku, L, Fv])), CompareColumnKind.NoMatch);

        Assert.Equal("alikula", row.Word);
        Assert.Equal("3SG PST cut FV", row.Gloss);
        Assert.Equal(ProjectStanding.Approved, row.Opinion);
        Assert.Equal("analysis-a-li-kul-a", row.FieldWorksAnalysisId);
        Assert.Equal(1, row.PanGlossReadingCount);
        Assert.Equal(3, row.Places);
        Assert.Equal(12, row.ElapsedMs);
        Assert.Null(row.IsUnread);
        Assert.Null(row.WarningsNamed);
        Assert.Equal("silfw://localhost/link?tool=Analyses", row.WordAnalysesLink);
    }

    [Fact]
    public void TheFieldWorksMorphemesKeepTheirAllomorphAndGrammaticalInfoIds()
    {
        var row = WordRowProjection.Of(Alikula(new ParserReading([A, Li, Ku, L, Fv])), CompareColumnKind.NoMatch);

        Assert.Equal(["form-a", "form-li", "form-kul", "form-fv"], row.FieldWorksMorphemes.Select(morph => morph.AllomorphId));
        Assert.Equal(["msa-a", "msa-li", "msa-kul", "msa-fv"], row.FieldWorksMorphemes.Select(morph => morph.GrammaticalInfoId));
    }

    [Fact]
    public void WhenDifferent_TheReadingClosestToFieldWorksIsShown()
    {
        var row = WordRowProjection.Of(
            Alikula(new ParserReading([Ku, L]), new ParserReading([A, Li, Ku, L, Fv])), CompareColumnKind.NoMatch);

        Assert.Equal(["a-", "li-", "ku-", "l", "-a"], row.PanGlossMorphemes.Select(morph => morph.Form));
        Assert.Equal(2, row.PanGlossReadingCount);
    }

    [Fact]
    public void WhenSame_PanGlossMorphemesAreNotRepeated()
    {
        var row = WordRowProjection.Of(Alikula(new ParserReading([A, Li, Kul, Fv])), CompareColumnKind.Match);

        Assert.Equal((WordRowOutcome.Same, "Kept", WordRowTone.Fine), (row.Outcome, row.Meaning, row.Tone));
        Assert.Empty(row.PanGlossMorphemes);
        Assert.Empty(row.DifferingPositions);
    }

    [Fact]
    public void AMorphemeWithNoIdentityAlwaysDiffers_EvenWhenSpelledTheSame()
    {
        var positions = WordRowProjection.DifferingPositions(
            [Morph("a-", "3SG", null), Kul], [Morph("a-", "3SG", null), Kul]);

        Assert.Equal([1], positions);
    }

    [Fact]
    public void WithNoFieldWorksAnalysis_NothingIsMarkedAsDiffering()
    {
        var word = Alikula(new ParserReading([A, Li, Ku, L, Fv])) with
        {
            ProjectStanding = ProjectStanding.NotPresent,
            ExpectedAnalysis = null,
            StoredAnalyses = [],
        };

        var row = WordRowProjection.Of(word, CompareColumnKind.NoMatch);

        Assert.Equal(("New: PanGloss proposes", WordRowTone.Look), (row.Meaning, row.Tone));
        Assert.Empty(row.FieldWorksMorphemes);
        Assert.Equal(5, row.PanGlossMorphemes.Count);
        Assert.Empty(row.DifferingPositions);
        Assert.Equal(string.Empty, row.Gloss);
    }

    [Fact]
    public void AReopenedWord_TakesTheApprovedStoredAnalysis_ElseItsOnlyOne()
    {
        var reopened = Alikula(new ParserReading([A, Li, Kul, Fv])) with
        {
            ExpectedAnalysis = null,
            StoredAnalyses = [Stored(ReadingGrade.Candidate, Ku, L), Stored(ReadingGrade.Approved, A, Li, Kul, Fv)],
        };
        var two = reopened with
        {
            StoredAnalyses = [Stored(ReadingGrade.Candidate, Ku, L), Stored(ReadingGrade.Candidate, Kul)],
        };
        var one = reopened with { StoredAnalyses = [Stored(ReadingGrade.Candidate, Ku, L)] };

        Assert.Equal(4, WordRowProjection.Of(reopened, CompareColumnKind.Match).FieldWorksMorphemes.Count);
        Assert.Empty(WordRowProjection.Of(two, CompareColumnKind.Match).FieldWorksMorphemes);
        Assert.Equal(["ku-", "l"], WordRowProjection.Of(one, CompareColumnKind.Match).FieldWorksMorphemes
            .Select(morph => morph.Form));
    }

    [Fact]
    public void AGlossFieldWorksDoesNotGiveShowsAQuestionMark()
    {
        var word = Alikula(new ParserReading([A])) with { ExpectedAnalysis = Stored(ReadingGrade.Approved, A, Morph("x", "", "x")) };

        Assert.Equal("3SG ?", WordRowProjection.Of(word, CompareColumnKind.Match).Gloss);
    }

    [Fact]
    public void ReadStateAndPlacesComeFromThePageWhenItKnowsThem()
    {
        var row = WordRowProjection.Of(Alikula(new ParserReading([A])), CompareColumnKind.Match,
            new WordRowFacts(Places: 7, IsUnread: true));

        Assert.Equal((7, true), (row.Places, row.IsUnread));
    }

    [Fact]
    public void ASkippedWordHasNoTime()
    {
        var word = new AssessmentWordResult("zz", "skipped", false, "Skipped", 0, null);

        var row = WordRowProjection.Of(word);

        Assert.Equal((WordRowOutcome.NotParsed, "Not parsed", WordRowTone.Neutral), (row.Outcome, row.Meaning, row.Tone));
        Assert.Null(row.ElapsedMs);
    }

    [Fact]
    public void WithoutAColumn_TheWordIsPlacedByTheCommandsMatcher()
    {
        var word = Alikula(new ParserReading([A, Li, Ku, L, Fv]));
        var placement = CompareSemantics.Place(new CompareWordFacts(word.ProjectStanding, word.Outcome,
            word.IsIncomplete, word.Morphology, word.ReadingGrades, word.MissedApproved?.Count ?? 0));

        Assert.Equal(WordRowProjection.OutcomeOf(placement.Column), WordRowProjection.Of(word).Outcome);
    }

    [Theory]
    [InlineData(CompareColumnKind.Match, WordRowOutcome.Same)]
    [InlineData(CompareColumnKind.NoMatch, WordRowOutcome.Different)]
    [InlineData(CompareColumnKind.NoParse, WordRowOutcome.NoParse)]
    [InlineData(CompareColumnKind.Timeout, WordRowOutcome.Stopped)]
    [InlineData(CompareColumnKind.Skipped, WordRowOutcome.NotParsed)]
    public void EachMatrixColumnIsOneOutcome(CompareColumnKind column, WordRowOutcome outcome) =>
        Assert.Equal(outcome, WordRowProjection.OutcomeOf(column));
}
