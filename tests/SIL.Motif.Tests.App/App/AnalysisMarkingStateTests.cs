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

    public static IEnumerable<object?[]> PrimaryActionCases =>
    [
        [Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("same", Book),
            AnalysisMarkingClass.Same, (AnalysisMarkingActionKind?)null, (string?)null, (string?)null, false],
        [Token(Stored(Book, ReadingGrade.Candidate, "stored-1")), Result("same", Book),
            AnalysisMarkingClass.Same, AnalysisMarkingActionKind.Approve, "Approve", ChangeKinds.Approve, true],
        [Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("none"),
            AnalysisMarkingClass.None, (AnalysisMarkingActionKind?)null, (string?)null, (string?)null, false],
        [Token(), Result("new", Child), AnalysisMarkingClass.Different,
            AnalysisMarkingActionKind.Add, "Add", ChangeKinds.AddCandidate, true],
        [Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("extra", Book, Child),
            AnalysisMarkingClass.Extra, AnalysisMarkingActionKind.KeepFieldWorks, "Keep", (string?)null, true],
        [Token(Stored(Book, ReadingGrade.Disapproved, "stored-1")), Result("different", Child),
            AnalysisMarkingClass.Different, AnalysisMarkingActionKind.Accept, "Accept", ChangeKinds.Approve, true],
        [Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("capped", true, Book),
            AnalysisMarkingClass.Capped, (AnalysisMarkingActionKind?)null, (string?)null, (string?)null, false],
    ];

    [Theory]
    [MemberData(nameof(PrimaryActionCases))]
    public void PrimaryActionAndNeedsALookFollowTheClassOpinionTable(TextToken token,
        AssessmentWordResult? result, AnalysisMarkingClass expectedClass, AnalysisMarkingActionKind? expectedKind,
        string? expectedLabel, string? expectedChangeKind, bool expectedNeedsALook)
    {
        var state = AnalysisMarkingState.Create(token, result);

        Assert.Equal(expectedClass, state.PanGlossClass);
        Assert.Equal(expectedKind, state.PrimaryAction?.Kind);
        Assert.Equal(expectedLabel, state.PrimaryAction?.Label);
        Assert.Equal(expectedChangeKind, state.PrimaryAction?.ChangeKind);
        Assert.Equal(expectedNeedsALook, state.NeedsALook);
    }

    [Fact]
    public void UnreadDoesNotDependOnWhetherAnActionIsAvailable()
    {
        var state = AnalysisMarkingState.Create(Token(), Result("capped", true, Book));

        Assert.Null(state.PrimaryAction);
        Assert.Empty(state.FixChoices);
        Assert.True(state.IsUnread);
        Assert.False(state.NeedsALook);
    }

    [Theory]
    [InlineData(ReadingGrade.Approved, true, AnalysisMarkingClass.Same)]
    [InlineData(ReadingGrade.Approved, false, AnalysisMarkingClass.Conflict)]
    [InlineData(ReadingGrade.Disapproved, true, AnalysisMarkingClass.Conflict)]
    [InlineData(ReadingGrade.Disapproved, false, AnalysisMarkingClass.None)]
    public void OpinionAndBuildEvidenceDetermineWhetherAnAnalysisConflicts(string opinion, bool isBuilt,
        AnalysisMarkingClass expectedClass)
    {
        var stored = Stored(Book, opinion, "stored-1");
        var result = isBuilt ? Result("built", Book) : opinion == ReadingGrade.Approved
            ? Result("alternative", Child) : Result("not-built");

        var state = AnalysisMarkingState.Create(Token(stored), result);

        Assert.Equal(expectedClass, state.PanGlossClass);
        if (opinion == ReadingGrade.Disapproved && isBuilt)
        {
            Assert.Null(state.PrimaryAction);
            Assert.NotEmpty(state.FixChoices);
        }
    }

    [Fact]
    public void MixedOpinionsUseTheConflictWhenOneBuiltAnalysisIsRejected()
    {
        var approved = Stored(Book, ReadingGrade.Approved, "approved");
        var disapproved = Stored(Book, ReadingGrade.Disapproved, "disapproved");

        var state = AnalysisMarkingState.Create(Token(approved, disapproved), Result("mixed", Book));

        Assert.Equal(AnalysisMarkingClass.Conflict, state.PanGlossClass);
        Assert.Null(state.PrimaryAction);
    }

    [Fact]
    public void IncorrectSpellingMakesEveryParserReadingAConflict()
    {
        var token = Token(Stored(Book, ReadingGrade.Approved, "stored-1")) with { IncorrectSpelling = true };

        var state = AnalysisMarkingState.Create(token, Result("spelling", Book));

        Assert.Equal(AnalysisMarkingClass.Conflict, state.PanGlossClass);
        Assert.Null(state.PrimaryAction);
    }

    [Theory]
    [InlineData(false, AnalysisMarkingClass.None)]
    [InlineData(true, AnalysisMarkingClass.Capped)]
    public void AnIncorrectSpellingThePanGlossDidNotParseIsNoConflict(bool capped, AnalysisMarkingClass expected)
    {
        var token = Token(Stored(Book, ReadingGrade.Approved, "stored-1")) with { IncorrectSpelling = true };

        var state = AnalysisMarkingState.Create(token, Result("spelling", capped));

        Assert.Equal(expected, state.PanGlossClass);
    }

    public static IEnumerable<object[]> AssessmentWordClassCases =>
    [
        [Assessment("analysed", ProjectStanding.IncorrectSpelling, false, ReadingGrade.Approved),
            AnalysisMarkingClass.Conflict],
        [Assessment("capped", ProjectStanding.Approved, true, ReadingGrade.Disapproved),
            AnalysisMarkingClass.Conflict],
        [Assessment("skipped", ProjectStanding.Approved, false,
            ReadingGrade.Approved, ReadingGrade.Candidate, ReadingGrade.Disapproved),
            AnalysisMarkingClass.Refused],
    ];

    [Theory]
    [MemberData(nameof(AssessmentWordClassCases))]
    public void AssessmentWordFixturesClassifyStoredOpinionsWithoutParserIds(
        AssessmentWordResult result, AnalysisMarkingClass expectedClass)
    {
        Assert.All(result.Readings ?? [], reading => Assert.Null(reading.StoredAnalysisId));

        var state = AnalysisMarkingState.Create(result);

        Assert.Equal(expectedClass, state.PanGlossClass);
        Assert.Equal(result.StoredAnalyses.Select(reading => reading.StoredAnalysisOpinion),
            state.FieldWorksAnalyses.Select(analysis => analysis.Opinion));
    }

    [Fact]
    public void PrimaryAddStagesUnknownWhileAddAsApprovedRemainsAFixChoice()
    {
        var state = AnalysisMarkingState.Create(Token(), Result("new", Child));

        Assert.Equal(ChangeKinds.AddCandidate, state.PrimaryAction!.ChangeKind);
        Assert.Equal("Unknown", state.PrimaryAction.AfterApply);
        Assert.Contains(state.FixChoices, choice => choice.Label == "Add as Approved" &&
            choice.ChangeKind == ChangeKinds.Approve);
    }

    [Fact]
    public void NoParseWithAnApprovedAnalysisOffersKeepAsAFixChoice()
    {
        var state = AnalysisMarkingState.Create(
            Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("none"));

        Assert.Contains(state.FixChoices, choice => choice.Kind == AnalysisMarkingActionKind.KeepFieldWorks &&
            choice.Label == "Keep FieldWorks");
        Assert.False(state.NeedsALook);
    }

    [Fact]
    public void NeedsALookTracksAvailableUnstagedActions()
    {
        var actionable = AnalysisMarkingState.Create(
            Token(Stored(Book, ReadingGrade.Candidate, "stored-1")), Result("book", Book));
        var staged = actionable.WithStagedTransitions(
            [new StagedMarkingTransition("Unknown", "Approved")]);
        var capped = AnalysisMarkingState.Create(
            Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("book", true, Book));

        Assert.True(actionable.NeedsALook);
        Assert.False(staged.NeedsALook);
        Assert.False(capped.NeedsALook);
    }

    [Fact]
    public void HoverSummaryUsesUnknownForTheCandidateGrade()
    {
        var token = new ResultsTokenViewModel("Text", 1,
            Token(Stored(Book, ReadingGrade.Candidate, "stored-1")), null);

        Assert.Contains("Unknown", token.HoverSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate", token.HoverSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ReadingGrade.Approved, "Approved")]
    [InlineData(ReadingGrade.Disapproved, "Disapproved")]
    [InlineData(ReadingGrade.Candidate, "Unknown")]
    public void HoverSummaryNamesEveryOpinionInTheWindowsWords(string grade, string label)
    {
        var token = new ResultsTokenViewModel("Text", 1, Token(Stored(Book, grade, "stored-1")), null);

        Assert.Contains($" · {label} · ", token.HoverSummary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ReadingGrade.Approved, "Approved")]
    [InlineData(ReadingGrade.Disapproved, "Disapproved")]
    [InlineData(ReadingGrade.Candidate, "Unknown")]
    public void RemovingAnAnalysisNamesItsOpinionInTheWindowsWords(string grade, string label)
    {
        var state = AnalysisMarkingState.Create(Token(Stored(Book, grade, "stored-1")), Result("book", Book));

        var remove = Assert.Single(state.FixChoices, choice => choice.Label == "Remove analysis");
        Assert.Equal($"{label} → Removed", remove.Subtitle);
        Assert.Equal(label, remove.Now);
    }

    [Fact]
    public void AgreementRemovalAndKeepChoicesDoNotNeedALook()
    {
        var agreement = AnalysisMarkingState.Create(
            Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("same", Book));
        var noParse = AnalysisMarkingState.Create(
            Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("none"));
        var capped = AnalysisMarkingState.Create(
            Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("capped", true, Book));

        Assert.False(agreement.NeedsALook);
        Assert.False(noParse.NeedsALook);
        Assert.False(capped.NeedsALook);
    }

    [Fact]
    public void AStoredAnalysisCanBeRemovedEvenWhenTheParserAgrees()
    {
        var stored = Stored(Book, ReadingGrade.Approved, "stored-1");

        var state = AnalysisMarkingState.Create(Token(stored), Result("book", Book));

        var remove = Assert.Single(state.FixChoices, choice => choice.Label == "Remove analysis");
        Assert.Equal("stored-1", remove.StoredAnalysisId);
        Assert.Equal("remove-analysis", remove.ChangeKind);
    }

    [Fact]
    public void ApprovedAnalysisCanReturnToUnknownThroughTheFixChoices()
    {
        var state = AnalysisMarkingState.Create(
            Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("book", Book));

        Assert.Contains(state.FixChoices, choice => choice.Kind == AnalysisMarkingActionKind.MakeUnknown &&
            choice.Label == "Make Unknown" && choice.StoredAnalysisId == "stored-1" &&
            choice.ChangeKind == ChangeKinds.Candidate && choice.Subtitle == "Approved → Unknown");
    }

    [Fact]
    public void DisapprovedToApprovedFixChoiceIsNamedApprove()
    {
        var state = AnalysisMarkingState.Create(
            Token(Stored(Book, ReadingGrade.Disapproved, "stored-1")), Result("none"));

        Assert.Contains(state.FixChoices, choice => choice.Kind == AnalysisMarkingActionKind.Approve &&
            choice.Label == "Approve" && choice.ChangeKind == ChangeKinds.Approve &&
            choice.Subtitle == "Disapproved → Approved");
    }

    [Fact]
    public void DisapprovedDifferentReadingUsesTheFixMenu()
    {
        var state = AnalysisMarkingState.Create(
            Token(Stored(Book, ReadingGrade.Disapproved, "stored-1")), Result("different", Child));

        Assert.Equal(["Approve", "Make Unknown", "Accept PanGloss's analysis", "Add as Unknown", "Add PanGloss's analyses as Unknown",
            "Keep FieldWorks", "Remove analysis"],
            state.FixChoices.Select(choice => choice.Label));
        Assert.Equal(ChangeKinds.Approve, state.FixChoices[2].ChangeKind);
        Assert.Equal(ChangeKinds.AddCandidate, state.FixChoices[3].ChangeKind);
    }

    [Fact]
    public void AnApprovedWordCanAddANewPanGlossReadingAsUnknownButNotApproveTheNewSet()
    {
        var state = AnalysisMarkingState.Create(
            Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("different", Child));

        Assert.Equal(["Disapprove", "Make Unknown", "Add as Unknown", "Keep FieldWorks", "Remove analysis"],
            state.FixChoices.Select(choice => choice.Label));
        Assert.DoesNotContain(state.FixChoices, choice => choice.ChangeKind == ChangeKinds.Approve ||
            choice.Kind == AnalysisMarkingActionKind.AcceptNewSet);
    }

    [Fact]
    public void MatchingUnknownAnalysisOffersOpinionActionsInTheFixMenu()
    {
        var state = AnalysisMarkingState.Create(Token(Stored(Book, ReadingGrade.Candidate, "stored-1")),
            Result("book", Book));

        Assert.Contains(state.FixChoices, choice => choice.Kind == AnalysisMarkingActionKind.Approve &&
            choice.StoredAnalysisId == "stored-1");
        Assert.Contains(state.FixChoices, choice => choice.Kind == AnalysisMarkingActionKind.Disapprove &&
            choice.StoredAnalysisId == "stored-1");
    }

    [Fact]
    public void TimedOutMatchedReadingHasNoDecision()
    {
        var state = AnalysisMarkingState.Create(
            Token(Stored(Book, ReadingGrade.Approved, "stored-1")), Result("timed-out", true, Book));

        Assert.Equal(AnalysisMarkingClass.Capped, state.PanGlossClass);
        Assert.Null(state.PrimaryAction);
        Assert.Contains(state.FixChoices, choice => choice.Kind == AnalysisMarkingActionKind.RemoveAnalysis);
        Assert.False(state.NeedsALook);
    }

    [Theory]
    [InlineData("uncertain", true, false)]
    [InlineData("no-longer-fits", false, true)]
    public void AStagedOpinionCarriesItsTransitionAndFitOverlay(string fit, bool uncertain, bool noLongerFits)
    {
        var state = AnalysisMarkingState.Create(Token(Stored(Book, ReadingGrade.Candidate, "stored-1")), Result("book", Book))
            .WithStagedTransitions([new StagedMarkingTransition("Unknown", "Approved", FitStatus: fit)]);

        Assert.Equal("Unknown → Approved", Assert.Single(state.StagedTransitions).Text);
        Assert.Equal(uncertain, state.IsUncertain);
        Assert.Equal(noLongerFits, state.NoLongerFits);
        Assert.True(state.IsUnread);
        Assert.False(state.NeedsALook);
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

    private static AssessmentWordResult Result(string word, params ParseAnalysis[] readings) => Result(word, false, readings);

    private static AssessmentWordResult Result(string word, bool incomplete, params ParseAnalysis[] readings) =>
        new(word, readings.Length == 0 ? "no-analysis" : "analysed", incomplete, "Complete", 3, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, word, 3, incomplete, false, false, readings, []),
            Readings = readings.Select(_ => new ParserReading(
                [new ParserReadingMorph("entry", "gloss", "n", null, false, "silfw://entry")])).ToArray(),
        };

    private static AssessmentWordResult Assessment(string outcome, string standing, bool incomplete,
        params string[] opinions)
    {
        var analyses = opinions.Select((opinion, index) =>
        {
            var reading = Reading($"form-{index + 1}", $"msa-{index + 1}");
            var id = $"stored-{index + 1}";
            return new ParserReading([new ParserReadingMorph($"form-{index + 1}", "book", "n", null, false, null)])
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
        }).ToArray();
        var parse = outcome is "analysed" or "capped" ? Reading("form-1", "msa-1") : null;
        return new AssessmentWordResult("word", outcome, incomplete, "Complete", 3, null)
        {
            ProjectStanding = standing,
            StoredAnalyses = analyses,
            Morphology = new ParseWordEvidence("v1", 0, "word", 3, incomplete, false, false,
                parse is null ? [] : [parse], []),
            Readings = parse is null ? [] : [new ParserReading(
                [new ParserReadingMorph("form-1", "book", "n", null, false, null)])],
        };
    }
}
