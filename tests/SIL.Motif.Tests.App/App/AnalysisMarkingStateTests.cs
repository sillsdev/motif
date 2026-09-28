using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class AnalysisMarkingStateTests
{
    private static readonly ParseAnalysis Book = Reading("form-1", "msa-1");
    private static readonly ParseAnalysis Child = Reading("form-2", "msa-2");

    public static IEnumerable<object[]> ClassificationCases =>
    [
        ["same", Token(Stored(Book, "approved", "stored-1")), Result("same", Book), AnalysisMarkingClass.Same,
            AnalysisMarkingActionKind.KeepFieldWorks],
        ["different", Token(Stored(Book, "approved", "stored-1")), Result("different", Child),
            AnalysisMarkingClass.Different, AnalysisMarkingActionKind.Add],
        ["extra", Token(Stored(Book, "approved", "stored-1")), Result("extra", Book, Child),
            AnalysisMarkingClass.Extra, AnalysisMarkingActionKind.KeepFieldWorks],
        ["none", Token(Stored(Book, "approved", "stored-1")), Result("none"),
            AnalysisMarkingClass.None, (AnalysisMarkingActionKind?)null],
        ["capped with a match", Token(Stored(Book, "unknown", "stored-1")), Result("capped", true, Book),
            AnalysisMarkingClass.Capped, AnalysisMarkingActionKind.Approve],
        ["capped with a returned extra", Token(Stored(Book, "approved", "stored-1")), Result("capped", true, Child),
            AnalysisMarkingClass.Capped, AnalysisMarkingActionKind.Add],
        ["not assessed", Token(Stored(Book, "approved", "stored-1")), (AssessmentWordResult?)null,
            AnalysisMarkingClass.NotAssessed, (AnalysisMarkingActionKind?)null],
    ];

    [Theory]
    [MemberData(nameof(ClassificationCases))]
    public void EachOccurrenceKeepsItsOwnFieldWorksAndPanGlossEvidence(string name, TextToken token,
        AssessmentWordResult? result, AnalysisMarkingClass expectedClass, AnalysisMarkingActionKind? expectedAction)
    {
        var state = AnalysisMarkingState.Create(token, result);

        Assert.Single(state.FieldWorksAnalyses);
        Assert.Equal(token.StoredAnalyses[0].StoredAnalysisOpinion, state.FieldWorksAnalyses[0].Opinion);
        Assert.Equal(expectedClass, state.PanGlossClass);
        Assert.Equal(expectedAction, state.PrimaryAction?.Kind);
        Assert.Equal(expectedClass != AnalysisMarkingClass.Same, state.NeedsALook);
        Assert.NotEmpty(name);
    }

    [Fact]
    public void FixChoicesNameOneReadingAndShowTheOpinionTransition()
    {
        var stored = Stored(Book, "unknown", "stored-1");
        var state = AnalysisMarkingState.Create(Token(stored), Result("book", Book));

        Assert.Contains(state.FixChoices, choice => choice.Kind == AnalysisMarkingActionKind.Approve &&
            choice.Label == "Approve" && choice.Subtitle == "Unknown → Approved" &&
            choice.StoredAnalysisId == stored.StoredAnalysisId);
        Assert.Equal(Book, Assert.Single(state.PanGlossReadings).Analysis);
        Assert.Equal("unknown", Assert.Single(state.PanGlossReadings).MatchingOpinions);
    }

    [Theory]
    [InlineData("uncertain", true, false)]
    [InlineData("no-longer-fits", false, true)]
    public void AStagedOpinionCarriesItsTransitionAndFitOverlay(string fit, bool uncertain, bool noLongerFits)
    {
        var state = AnalysisMarkingState.Create(Token(Stored(Book, "unknown", "stored-1")), Result("book", Book))
            .WithStagedTransition("Unknown", "Approved", fit);

        Assert.Equal("Unknown → Approved", state.StagedTransition!.Text);
        Assert.Equal(uncertain, state.IsUncertain);
        Assert.Equal(noLongerFits, state.NoLongerFits);
        Assert.True(state.NeedsALook);
    }

    [Fact]
    public void KeepFieldWorksDoesNotStageAChange()
    {
        var state = AnalysisMarkingState.Create(Token(Stored(Book, "approved", "stored-1")), Result("book", Book));

        Assert.Same(state, state.KeepFieldWorks());
        Assert.Null(state.StagedTransition);
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
            Occurrence = new OccurrenceAnchor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0),
            WordformId = "wordform-1",
        };

    private static AssessmentWordResult Result(string word, params ParseAnalysis[] readings) => Result(word, false, readings);

    private static AssessmentWordResult Result(string word, bool incomplete, params ParseAnalysis[] readings) =>
        new(word, readings.Length == 0 ? "no-analysis" : "analysed", incomplete, "Complete", 3, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, word, 3, incomplete, false, false, readings, []),
            Readings = readings.Select(_ => new ParserReading(
                [new ParserReadingMorph("entry", "gloss", "n", null, false, "silfw://entry")])).ToArray(),
        };
}
