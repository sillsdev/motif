using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ResultsTokenViewModelTests
{
    private static readonly BaselineToken Baseline = new("project", "sha256:" + new string('a', 64), "1",
        "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    private static readonly Guid MorphGuid = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly ParseAnalysis Book = new([new ParseMorph("form-1", "msa-1", null, null)]);
    private static readonly ParseAnalysis Built = new([new ParseMorph(MorphGuid.ToString("D"), "msa-2", null, null)]);

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
        };

    private static AssessmentWordResult Result(params ParseAnalysis[] readings) =>
        new("book", "analysed", false, "Complete", 3, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, "book", 3, false, false, false, readings, []),
            Readings = readings.Select(_ => new ParserReading(
                [new ParserReadingMorph("entry", "gloss", "n", null, false, "silfw://entry")])).ToArray(),
        };

    private static TrialEvidence Trial(AssessmentWordResult? word, TrialFreshness freshness = TrialFreshness.Current,
        DryRunProjection? dryRun = null)
    {
        var record = new TrialRecord("job/trial", "proposal/one", "sha256:intent", Baseline, dryRun,
            new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "trial-root" }, [], false, null,
            SIL.Motif.Contract.Jobs.JobStatus.Completed, DateTimeOffset.UnixEpoch, freshness);
        var set = new EvidenceSet(Baseline, "trial-root", [], new Dictionary<string, string>(), null, [],
            new EvidenceOrigin(EvidenceOriginKind.Trial, record.ProposalId, record.IntentDigest, record.JobId));
        return new TrialEvidence(set, record, word is null ? [] : [word]);
    }

    private static EvidenceLens Lens(TrialEvidence? trial, bool inProposalMode = true) =>
        new(new ProjectEvidence(), trial, MarkupView.TodayToProposal, inProposalMode);

    [Fact]
    public void TheCardShowsInPanGlossWithThisProposalInProposalMode()
    {
        using var token = new ResultsTokenViewModel("Text", 1,
            Token(Stored(Book, ReadingGrade.Candidate, "stored-1")), Result(Book));

        token.ShowProposal(Lens(Trial(Result(Book, Built))));

        Assert.True(token.ShowsProposalBlock);
        Assert.Equal(2, token.ProposalReadings.Count);
        Assert.True(token.ProposalReading.HasAfter);
        var approve = Assert.IsType<WordMarkingChoice>(token.ApproveInProposal);
        Assert.StartsWith("Approve ", approve.Label, StringComparison.Ordinal);
        Assert.EndsWith(" in this Proposal", approve.Label, StringComparison.Ordinal);
        Assert.Equal("Add an expected parse to this Proposal…", Assert.IsType<WordMarkingChoice>(token.AddExpectedParse).Label);
        Assert.Equal("stored-1", approve.Choice.StoredAnalysisId);
        var add = Assert.IsType<WordMarkingChoice>(token.AddExpectedParse);
        Assert.Equal("Add an expected parse to this Proposal…", add.Label);
        Assert.Contains(ProposalReadingBlock.WillAddAsApproved, add.Subtitle, StringComparison.Ordinal);
        Assert.True(token.Disposition.HasProposalActions);
    }

    [Fact]
    public void OutsideProposalModeTheCardIsUnchanged()
    {
        using var token = new ResultsTokenViewModel("Text", 1,
            Token(Stored(Book, ReadingGrade.Candidate, "stored-1")), Result(Book));
        var before = token.Marking;

        token.ShowProposal(Lens(null, inProposalMode: false));

        Assert.False(token.ShowsProposalBlock);
        Assert.Null(token.Marking.ProposalBlock);
        Assert.False(token.Disposition.HasProposalActions);
        Assert.Equal(before.SetChoices, token.Marking.SetChoices);
    }

    [Fact]
    public void AProposalWithNoTrialSaysNotTriedYetAndOffersNoAction()
    {
        using var token = new ResultsTokenViewModel("Text", 1, Token(), Result(Book));

        token.ShowProposal(Lens(null));

        Assert.True(token.ShowsProposalBlock);
        Assert.Equal(TrialStandingWords.NotTried.Status, token.ProposalStatus);
        Assert.False(token.ProposalReading.HasAfter);
        Assert.Empty(token.BoundProposalChoices);
    }

    [Theory]
    [InlineData(TrialFreshness.BaselineMoved)]
    [InlineData(TrialFreshness.ProposalChanged)]
    public void AStaleTrialStillShowsItsReadingAndSaysNeedsAnotherTry(TrialFreshness freshness)
    {
        using var token = new ResultsTokenViewModel("Text", 1, Token(), Result(Book));

        token.ShowProposal(Lens(Trial(Result(Built), freshness)));

        Assert.Equal(TrialStandingWords.For(TrialStanding.From(SIL.Motif.Contract.Jobs.JobStatus.Completed, freshness)).Status,
            token.ProposalStatus);
        Assert.True(token.HasProposalReadings);
    }

    [Fact]
    public void BuiltByNamesTheGrammarEntry()
    {
        var dryRun = new DryRunProjection("proposal/one", "sha256:intent", "", [], "e", "f")
        {
            Operations =
            [
                new ProposalOperationView("op-1", "lexical/lexEntry/createLexemeForm", null,
                    CanonicalId.FromGuid(MorphGuid).Value, [], null),
            ],
        };
        using var token = new ResultsTokenViewModel("Text", 1, Token(), Result(Book));

        token.ShowProposal(Lens(Trial(Result(Built), dryRun: dryRun)),
            reading => ProposalBuiltBy.For(Trial(null, dryRun: dryRun), reading));

        var reading = Assert.Single(token.ProposalReadings);
        var entry = Assert.Single(reading.BuiltBy);
        Assert.Equal("op-1", entry.OperationId);
        Assert.Equal(ChangeVocabulary.Of("lexical/lexEntry/createLexemeForm").Title, entry.Title);
    }

    [Fact]
    public void TheSetMenuNamesTheProposalWhenItsTrialHasALabel()
    {
        using var token = new ResultsTokenViewModel("Text", 1,
            Token(Stored(Book, ReadingGrade.Candidate, "stored-1")), Result(Book));
        var labelled = Trial(Result(Book, Built));
        labelled = labelled with { Trial = labelled.Trial with { Label = "Sound change" } };

        token.ShowProposal(Lens(labelled));

        Assert.EndsWith(" in Sound change", token.ApproveInProposal!.Label, StringComparison.Ordinal);
        Assert.Equal("Add an expected parse to Sound change…", token.AddExpectedParse!.Label);
    }

    [Fact]
    public void ARunningTryAgainReadsTryingAndAFailedOneSaysDidNotFinish()
    {
        using var token = new ResultsTokenViewModel("Text", 1, Token(), Result(Book));

        token.ShowProposal(Lens(Trial(Result(Built))), standing: TrialStanding.Trying);
        Assert.Equal(TrialStandingWords.Trying.Status, token.ProposalStatus);
        Assert.True(token.HasProposalReadings);

        token.ShowProposal(Lens(null), standing: TrialStanding.Failed);
        Assert.Equal(TrialStandingWords.Failed.Status, token.ProposalStatus);

        token.ShowProposal(Lens(Trial(Result(Built))), standing: TrialStanding.Current);
        Assert.Equal(string.Empty, token.ProposalStatus);
    }

    [Fact]
    public void TheProposalReadingCarriesMorphsLikeEveryOtherReading()
    {
        using var token = new ResultsTokenViewModel("Text", 1, Token(), Result(Book));

        token.ShowProposal(Lens(Trial(Result(Built))));

        Assert.All(token.ProposalReadings, reading => Assert.True(reading.HasMorphs));
    }

    [Fact]
    public async Task AddAnExpectedParseStagesAddAndApproveWithTheTrialsAssessmentId()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        await changes.TargetAsync("proposal/one");
        using var token = new ResultsTokenViewModel("Text", 1, Token(), Result(Book));
        token.ShowProposal(Lens(Trial(Result(Built))));
        var add = Assert.IsType<WordMarkingChoice>(token.AddExpectedParse);

        await changes.AddFromMarkingAsync(new AnalysisMarkingAction(add.Choice.Kind, add.Label,
            add.Choice.StoredAnalysisId, add.Choice.Reading, add.Choice.ReadingIndex, add.Choice.Now,
            add.Choice.AfterApply, add.Choice.ChangeKind), add.Target);

        var put = Assert.Single(fake.PendingPutRequests);
        Assert.Equal("proposal/one", put.ProposalId);
        Assert.Equal(ChangeKinds.Approve, put.Change.Kind);
        Assert.Equal("trial-root", put.Change.AssessmentId);
        Assert.Equal(Built, put.Change.Reading);
        Assert.Equal(0, put.Change.ReadingIndex);
        Assert.Equal(new TrialEvidenceExpectation("proposal/one", "sha256:intent", "trial-root"),
            put.ExpectedContext?.Trial);
    }

    [Fact]
    public async Task ApproveInThisProposalStagesIntoTheOpenProposal()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        await changes.TargetAsync("proposal/one");
        using var token = new ResultsTokenViewModel("Text", 1,
            Token(Stored(Book, ReadingGrade.Candidate, "stored-1")), Result(Book));
        token.ShowProposal(Lens(Trial(Result(Book))));
        var approve = Assert.IsType<WordMarkingChoice>(token.ApproveInProposal);

        await changes.AddFromMarkingAsync(new AnalysisMarkingAction(approve.Choice.Kind, approve.Label,
            approve.Choice.StoredAnalysisId, approve.Choice.Reading, approve.Choice.ReadingIndex,
            approve.Choice.Now, approve.Choice.AfterApply, approve.Choice.ChangeKind), approve.Target);

        var put = Assert.Single(fake.PendingPutRequests);
        Assert.Equal("proposal/one", put.ProposalId);
        Assert.Equal("stored-1", put.Change.StoredAnalysisId);
        Assert.Equal(ChangeKinds.Approve, put.Change.Kind);
    }

    [Fact]
    public async Task LeavingProposalModeStagesIntoThePendingDraftAgain()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        await changes.TargetAsync("proposal/one");

        await changes.TargetAsync(null);

        Assert.Null(changes.TargetProposalId);
        Assert.Null(fake.PendingLoadRequests.Last().ProposalId);
        Assert.Equal("proposal/one", fake.PendingLoadRequests[^2].ProposalId);
    }

    [Fact]
    public async Task ATraceAgainstAGoneScratchOffersTryAgain()
    {
        var fake = new FakeCommandClient();
        fake.OnTraceWord((_, _) => Task.FromResult(CommandOutcome<WordTraceResponse>.Refused(
            new Refusal("trial.scratch-gone", FailureReason.NotFound, "The Proposal's copy is gone. Try it again."))));
        var trace = new TraceWordViewModel(fake) { ProposalIdSource = () => "proposal/one" };
        trace.SetProjectPath("project.fwdata");
        trace.SetWord("book");

        await trace.TryCommand.ExecuteAsync(null);

        Assert.Equal("proposal/one", Assert.Single(fake.TraceWordRequests).ProposalId);
        Assert.True(trace.OffersTryAgain);
        Assert.Equal("The copy these changes were tried on is gone. Try them again.", trace.ShownRefusal?.Sentence);
    }
}
