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
    public void RebuiltDisapprovedReadingNeverConfirmsAnUndecidedAnalysis()
    {
        var candidate = Stored(ReadingGrade.Candidate, A) with
        {
            Identity = new ApprovedMorphology([new ApprovedMorph("candidate", "msa", null, [])]),
        };
        var disapproved = Stored(ReadingGrade.Disapproved, Kul) with
        {
            Identity = new ApprovedMorphology([new ApprovedMorph("disapproved", "msa", null, [])]),
        };
        var word = new AssessmentWordResult("word", "analysed", false, "Search completed", 1, null)
        {
            ProjectStanding = ProjectStanding.Candidate,
            StoredAnalyses = [candidate, disapproved],
            ReadingGrades = [ReadingGrade.Disapproved],
            Morphology = new ParseWordEvidence("v1", 0, "word", 1, false, false, false,
                [new ParseAnalysis([new ParseMorph("disapproved", "msa", null, null)])], []),
        };

        var row = WordRowProjection.Of(word);

        Assert.Equal(WordRowOutcome.Different, row.Outcome);
        Assert.Equal("Rebuilt an analysis you Disapproved", row.Meaning);
        Assert.Equal(WordRowTone.Problem, row.Tone);
    }

    [Fact]
    public void FixFirstExplainsARebuiltDisapprovedAnalysisWithoutInventingAMissingApprovedOne()
    {
        var facts = new CompareWordFacts(ProjectStanding.Approved, "analysed", false,
            new ParseWordEvidence("v1", 0, "word", 1, false, false, false,
                [new ParseAnalysis([new ParseMorph("form", "msa", null, null)])], []),
            [ReadingGrade.Approved], 0)
        {
            AnalysisComparison = new WordAnalysisComparison(
                [new ComparedReading(0, [new("approved", ReadingGrade.Approved),
                    new("disapproved", ReadingGrade.Disapproved)], ReadingGrade.Approved)], [], []),
        };

        var priority = CompareSemantics.FixFirst(facts, []);

        Assert.NotNull(priority);
        Assert.Equal("Rebuilt an analysis you Disapproved", priority.Explanation);
    }

    [Fact]
    public void MeaningGroupsUseStableIdentityWhenWordingCoincidesOrChanges()
    {
        var rows = new[]
        {
            new ObjectUseWord(new WordRow("a", WordRowOutcome.NoParse, "same translation", WordRowTone.Look)
                { MeaningCode = "lost" }),
            new ObjectUseWord(new WordRow("b", WordRowOutcome.Same, "same translation", WordRowTone.Look)
                { MeaningCode = "confirmed" }),
            new ObjectUseWord(new WordRow("c", WordRowOutcome.NoParse, "revised translation", WordRowTone.Look)
                { MeaningCode = "lost" }),
        };

        var grouped = ObjectUsesQuery.Split(rows);

        Assert.Equal(2, grouped.ByMeaning.Count);
        Assert.Equal(2, grouped.ByMeaning.Single(group => group.MeaningCode == "lost").Words);
        Assert.Equal("Lost", grouped.ByMeaning.Single(group => group.MeaningCode == "lost").Meaning);
        Assert.Equal(WordRowTone.Problem, grouped.ByMeaning.Single(group => group.MeaningCode == "lost").Tone);
    }

    [Theory]
    [InlineData("form-A", "form-a", false)]
    [InlineData("{AAAAAAAA-0000-0000-0000-000000000001}", "aaaaaaaa-0000-0000-0000-000000000001", true)]
    public void AlignmentUsesCanonicalGuidsAndExactOpaqueKeys(string left, string right, bool shared)
    {
        var first = A with { AllomorphId = left };
        var second = A with { AllomorphId = right };
        Assert.Equal(shared, Assert.Single(WordRowProjection.Align([first], [second])).Shared);
    }

    [Fact]
    public void ForAlikula_FieldWorksKulAgainstPanGlossKuAndL_MarksPositionsThreeAndFour()
    {
        var row = WordRowProjection.Of(Alikula(new ParserReading([A, Li, Ku, L, Fv])));

        Assert.Equal(WordRowOutcome.Different, row.Outcome);
        Assert.Equal(["a-", "li-", "kul", "-a"], row.FieldWorksMorphemes.Select(morph => morph.Form));
        Assert.Equal(["a-", "li-", "ku-", "l", "-a"], row.PanGlossMorphemes.Select(morph => morph.Form));
        Assert.Equal([3, 4], row.DifferingPositions);
        Assert.Equal(("Built something else", WordRowTone.Problem), (row.Meaning, row.Tone));
    }

    [Fact]
    public void ForAlikula_TheAlignmentPairsKulWithKuAndL_AndMatchesTheDifferingPositions()
    {
        IReadOnlyList<ParserReadingMorph> fieldWorks = [A, Li, Kul, Fv];
        IReadOnlyList<ParserReadingMorph> panGloss = [A, Li, Ku, L, Fv];

        var segments = WordRowProjection.Align(fieldWorks, panGloss);

        Assert.Equal(["a-|a-|shared", "li-|li-|shared", "kul|ku- l|parted", "-a|-a|shared"], segments.Select(segment =>
            $"{Forms(fieldWorks, segment.FieldWorks)}|{Forms(panGloss, segment.PanGloss)}|{(segment.Shared ? "shared" : "parted")}"));
        Assert.Equal(WordRowProjection.DifferingPositions(fieldWorks, panGloss),
            segments.Where(segment => !segment.Shared).SelectMany(segment => segment.PanGloss).Select(index => index + 1));
    }

    [Fact]
    public void TheAlignmentKeepsAMorphemeOnlyOneSideHas_AsItsOwnPartedSegment()
    {
        IReadOnlyList<ParserReadingMorph> fieldWorks = [A, Kul, Fv];
        IReadOnlyList<ParserReadingMorph> panGloss = [A, Li, Kul];

        var segments = WordRowProjection.Align(fieldWorks, panGloss);

        Assert.Equal(["a-|a-|shared", "|li-|parted", "kul|kul|shared", "-a||parted"], segments.Select(segment =>
            $"{Forms(fieldWorks, segment.FieldWorks)}|{Forms(panGloss, segment.PanGloss)}|{(segment.Shared ? "shared" : "parted")}"));
    }

    [Fact]
    public void WithOneSideEmpty_TheAlignmentIsOnePartedSegment_OrNothing()
    {
        Assert.Empty(WordRowProjection.Align([], []));
        var segment = Assert.Single(WordRowProjection.Align([], [Ku, L]));
        Assert.Empty(segment.FieldWorks);
        Assert.Equal([0, 1], segment.PanGloss);
        Assert.False(segment.Shared);
    }

    private static string Forms(IReadOnlyList<ParserReadingMorph> morphs, IReadOnlyList<int> indices) =>
        string.Join(" ", indices.Select(index => morphs[index].Form));

    [Fact]
    public void TheRowCarriesTheWordsFormGlossOpinionPlacesTimeAndWordAnalysesLink()
    {
        var row = WordRowProjection.Of(Alikula(new ParserReading([A, Li, Ku, L, Fv])));

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
        var row = WordRowProjection.Of(Alikula(new ParserReading([A, Li, Ku, L, Fv])));

        Assert.Equal(["form-a", "form-li", "form-kul", "form-fv"], row.FieldWorksMorphemes.Select(morph => morph.AllomorphId));
        Assert.Equal(["msa-a", "msa-li", "msa-kul", "msa-fv"], row.FieldWorksMorphemes.Select(morph => morph.GrammaticalInfoId));
    }

    [Fact]
    public void WhenDifferent_TheReadingClosestToFieldWorksIsShown()
    {
        var row = WordRowProjection.Of(
            Alikula(new ParserReading([Ku, L]), new ParserReading([A, Li, Ku, L, Fv])));

        Assert.Equal(["a-", "li-", "ku-", "l", "-a"], row.PanGlossMorphemes.Select(morph => morph.Form));
        Assert.Equal(2, row.PanGlossReadingCount);
    }

    [Fact]
    public void WhenSame_PanGlossMorphemesAreNotRepeated()
    {
        var row = WordRowProjection.Of(Alikula(new ParserReading([A, Li, Kul, Fv])) with
        {
            ReadingGrades = [ReadingGrade.Approved],
        });

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

        var row = WordRowProjection.Of(word);

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

        Assert.Equal(4, WordRowProjection.Of(reopened).FieldWorksMorphemes.Count);
        Assert.Empty(WordRowProjection.Of(two).FieldWorksMorphemes);
        Assert.Equal(["ku-", "l"], WordRowProjection.Of(one).FieldWorksMorphemes
            .Select(morph => morph.Form));
    }

    [Fact]
    public void AGlossFieldWorksDoesNotGiveShowsAQuestionMark()
    {
        var word = Alikula(new ParserReading([A])) with { ExpectedAnalysis = Stored(ReadingGrade.Approved, A, Morph("x", "", "x")) };

        Assert.Equal("3SG ?", WordRowProjection.Of(word).Gloss);
    }

    [Fact]
    public void ReadStateAndPlacesComeFromThePageWhenItKnowsThem()
    {
        var row = WordRowProjection.Of(Alikula(new ParserReading([A])),
            new WordRowFacts(Places: 7, IsUnread: true));

        Assert.Equal((7, true), (row.Places, row.IsUnread));
    }

    [Fact]
    public void ASkippedWordHasNoTime()
    {
        var word = new AssessmentWordResult("zz", "skipped", false, "Skipped", 0, null);

        var row = WordRowProjection.Of(word);

        Assert.Equal((WordRowOutcome.NoParse, ParserRefusals.Title, WordRowTone.Neutral), (row.Outcome, row.Meaning, row.Tone));
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
