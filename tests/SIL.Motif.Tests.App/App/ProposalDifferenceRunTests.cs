using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

// A Difference being made keeps running while the Proposals page refreshes, and ends with the project.
public sealed class ProposalDifferenceRunTests
{
    private const string Project = @"C:\projects\one.fwdata";
    private static readonly Guid GenesisId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ProposalCard Card(string id) =>
        new(id, id, "proposed", ProposalGroup.WaitingForYou, false, [new("rule", 1, "1 rule")],
            TrialStanding.Current, null, []);

    private sealed class Running
    {
        public FakeCommandClient Fake { get; } = new();

        public WorkspaceContext Context { get; private set; } = null!;

        public ProposalsPageModel Page { get; private set; } = null!;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken Token { get; private set; }

        public async Task OpenAsync()
        {
            Fake.ProposalStandingsCompleteWith(new ProposalStandings([Card("p/one"), Card("p/two")], 0));
            Fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(GenesisId, "Genesis 1")],
                HasBaseline: true));
            var fixture = new ProposalDifferenceFixture { TextId = GenesisId.ToString("D") };
            Fake.OnProposalDifference((_, _) => Task.FromResult(CommandOutcome<ProposalDifferenceResponse>.Refused(
                new Refusal(RefusalCodes.DifferenceNone, FailureReason.NotFound, "None."))));
            Fake.OnProduceProposalDifference(async (request, _, token) =>
            {
                Token = token;
                Started.TrySetResult();
                await Release.Task;
                return await fixture.Read(new ProposalDifferenceShowRequest(request.ProjectPath, request.ProposalId), token);
            });
            Context = WorkspaceContextTests.NewContext(Fake);
            Page = new ProposalsPageModel(Context);
            await Context.OpenProjectAsync(Project);
            await Page.DifferenceReading;
            Assert.Equal("p/one", Page.SelectedProposal!.ProposalId);
        }

        public async Task<Task> StartAsync()
        {
            var run = Page.Difference.Maker.RunCommand.ExecuteAsync(null);
            await Started.Task;
            Assert.Equal(RunState.Running, Page.Difference.Maker.State);
            return run;
        }

        public async Task SelectAsync(string proposalId)
        {
            Page.SelectProposalCommand.Execute(Page.Groups.SelectMany(group => group.Cards)
                .Single(card => card.ProposalId == proposalId));
            await Page.DifferenceReading;
            Assert.Equal(proposalId, Page.SelectedProposal!.ProposalId);
        }

        public void AssertStillRunning(string proposalId = "p/one")
        {
            Assert.Equal(RunState.Running, Page.Difference.Maker.State);
            Assert.False(Token.IsCancellationRequested);
            Assert.Equal(proposalId, Page.SelectedProposal!.ProposalId);
        }

        public async Task FinishAsync(Task run)
        {
            Release.TrySetResult();
            await run;
        }
    }

    [Fact]
    public async Task ATrialProgressReportOnAnotherProposalLeavesARunningDifferenceRunning()
    {
        var scene = new Running();
        await scene.OpenAsync();
        var before = scene.Page.SelectedProposal;
        var run = await scene.StartAsync();

        var states = scene.Context.TrialRun.States;
        scene.Context.TrialRun.States = states.SetItem("p/two", TrialRunState.Idle("p/two"));
        scene.Context.TrialRun.States = scene.Context.TrialRun.States.SetItem("p/one", TrialRunState.Idle("p/one"));

        scene.AssertStillRunning();
        Assert.NotSame(before, scene.Page.SelectedProposal);
        await scene.FinishAsync(run);
        Assert.Equal(RunState.Completed, scene.Page.Difference.Maker.State);
        Assert.Equal(ProposalDifferenceState.Shown, scene.Page.Difference.State);
    }

    [Fact]
    public async Task ABaselineCaptureOrAnEvidencePublishDuringARunLeavesItRunning()
    {
        var scene = new Running();
        await scene.OpenAsync();
        var run = await scene.StartAsync();

        await scene.Context.PublishBaselineCapturedAsync(openFirstRunSetup: false);
        scene.AssertStillRunning();

        await scene.Page.EvidencePublishedAsync(scene.Context.EvidenceLens, CancellationToken.None);
        scene.AssertStillRunning();

        await scene.FinishAsync(run);
        Assert.Equal(RunState.Completed, scene.Page.Difference.Maker.State);
        Assert.Equal(ProposalDifferenceState.Shown, scene.Page.Difference.State);
    }

    [Fact]
    public async Task ARunOnAProposalThatIsNotFirstSurvivesABaselineCaptureAndAnEvidencePublish()
    {
        var scene = new Running();
        await scene.OpenAsync();
        await scene.SelectAsync("p/two");
        var run = await scene.StartAsync();

        await scene.Context.PublishBaselineCapturedAsync(openFirstRunSetup: false);
        scene.AssertStillRunning("p/two");

        await scene.Page.EvidencePublishedAsync(scene.Context.EvidenceLens, CancellationToken.None);
        scene.AssertStillRunning("p/two");

        await scene.FinishAsync(run);
        Assert.Equal(RunState.Completed, scene.Page.Difference.Maker.State);
        Assert.Equal(ProposalDifferenceState.Shown, scene.Page.Difference.State);
        Assert.Equal("p/two", scene.Page.SelectedProposal!.ProposalId);
    }

    [Fact]
    public async Task ARefreshThatRemovesTheProposalEndsTheDifferenceAndItsRun()
    {
        var scene = new Running();
        await scene.OpenAsync();
        var run = await scene.StartAsync();

        scene.Fake.ProposalStandingsCompleteWith(new ProposalStandings([Card("p/two")], 0));
        await scene.Context.PublishBaselineCapturedAsync(openFirstRunSetup: false);

        Assert.Equal("p/two", scene.Page.SelectedProposal!.ProposalId);
        Assert.True(scene.Token.IsCancellationRequested);
        await scene.FinishAsync(run);
        Assert.NotEqual(RunState.Completed, scene.Page.Difference.Maker.State);
        Assert.NotEqual(ProposalDifferenceState.Shown, scene.Page.Difference.State);
    }

    [Fact]
    public async Task ChoosingAnotherProposalCancelsTheRunAndNeverShowsItsResult()
    {
        var scene = new Running();
        await scene.OpenAsync();
        var run = await scene.StartAsync();

        scene.Page.SelectProposalCommand.Execute(scene.Page.Groups.SelectMany(group => group.Cards)
            .Single(card => card.ProposalId == "p/two"));

        Assert.True(scene.Token.IsCancellationRequested);
        Assert.Equal(RunState.Idle, scene.Page.Difference.Maker.State);
        await scene.FinishAsync(run);
        Assert.Equal(RunState.Idle, scene.Page.Difference.Maker.State);
        Assert.Null(scene.Page.Difference.Summary);
    }

    [Fact]
    public async Task StoppingTheProjectsWorkReturnsOnlyAfterARunningDifferenceHasEnded()
    {
        var scene = new Running();
        await scene.OpenAsync();
        var run = await scene.StartAsync();

        var stopping = scene.Context.StopProjectWorkAsync();
        await Task.Delay(50);

        Assert.True(scene.Token.IsCancellationRequested);
        Assert.False(stopping.IsCompleted);
        Assert.False(run.IsCompleted);

        scene.Release.SetResult();
        await stopping;

        Assert.True(run.IsCompleted);
    }

    private static readonly Guid ExodusId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // A page whose standings and saved Difference a test changes between refreshes.
    private sealed class Shown
    {
        public FakeCommandClient Fake { get; } = new();

        public List<ProposalCard> Cards { get; } = [Card("p/one"), Card("p/two")];

        public ProposalDifferenceFixture Fixture { get; }

        public WorkspaceContext Context { get; private set; } = null!;

        public ProposalsPageModel Page { get; private set; } = null!;

        public Shown(ProposalDifferenceFixture? fixture = null) =>
            Fixture = fixture ?? new ProposalDifferenceFixture { TextId = GenesisId.ToString("D") };

        public async Task OpenAsync()
        {
            Fake.OnProposalStandings((_, _) => Task.FromResult(CommandOutcome<ProposalStandings>.Success(
                new ProposalStandings([.. Cards], 0))));
            Fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(GenesisId, "Genesis 1")],
                HasBaseline: true));
            Fake.OnProposalDifference(Fixture.Read);
            Context = WorkspaceContextTests.NewContext(Fake);
            Page = new ProposalsPageModel(Context);
            await Context.OpenProjectAsync(Project);
            await Page.DifferenceReading;
        }
    }

    [Fact]
    public async Task AProposalDecidedWhileShownLosesItsWordListAndFilterChipsOnTheNextRefresh()
    {
        var scene = new Shown();
        await scene.OpenAsync();
        var difference = scene.Page.Difference;
        Assert.Equal(ProposalDifferenceState.Shown, difference.State);
        Assert.NotEmpty(difference.Words);
        Assert.True(difference.CheckChips.Count > 0);

        scene.Fixture.WordDetailKept = false;
        scene.Cards[0] = scene.Cards[0] with { Group = ProposalGroup.SetAside, Status = "withdrawn" };
        await scene.Context.PublishBaselineCapturedAsync(openFirstRunSetup: false);

        Assert.Equal("p/one", scene.Page.SelectedProposal!.ProposalId);
        Assert.Equal(ProposalDifferenceState.Shown, difference.State);
        Assert.False(difference.WordDetailKept);
        Assert.True(difference.IsSummaryOnly);
        Assert.Empty(difference.Words);
        Assert.False(difference.OffersMake);
    }

    [Fact]
    public async Task ADeferredCardShownWithOnlyItsSummaryOffersToMakeTheDifferenceAgain()
    {
        var scene = new Shown();
        scene.Fixture.WordDetailKept = false;
        scene.Cards[0] = scene.Cards[0] with { Status = "deferred" };
        await scene.OpenAsync();
        var difference = scene.Page.Difference;

        Assert.True(difference.IsSummaryOnly);
        Assert.True(difference.OffersMake);
        Assert.True(difference.ShowsRemakeHint);
    }

    [Fact]
    public async Task ADeferredProposalWithRemadeWordsLosesThemWhenItIsDeclinedWhileShown()
    {
        var scene = new Shown();
        scene.Cards[0] = scene.Cards[0] with { Status = "deferred" };
        await scene.OpenAsync();
        var difference = scene.Page.Difference;
        Assert.NotEmpty(difference.Words);
        Assert.True(difference.OffersMake);

        scene.Fixture.WordDetailKept = false;
        scene.Cards[0] = scene.Cards[0] with { Group = ProposalGroup.SetAside, Status = "rejected" };
        await scene.Context.PublishBaselineCapturedAsync(openFirstRunSetup: false);

        Assert.Equal("p/one", scene.Page.SelectedProposal!.ProposalId);
        Assert.True(difference.IsSummaryOnly);
        Assert.Empty(difference.Words);
        Assert.False(difference.OffersMake);
    }

    private static ProposalDifferenceFixture SourceValidatedWithUnreadable() => new()
    {
        TextId = GenesisId.ToString("D"), Reference = ProposalDifferenceReferences.SourceValidated,
        Warnings = [ProposalDifferenceWarnings.EvidenceUnreadable + ": 'Stage sita' (proposal/9)."],
        Unreadable = [new("proposal/9", "Stage sita", "The stored revision could not be read.")],
    };

    [Fact]
    public async Task ASourceValidatedProposalDecidedWhileShownKeepsItsNotesAndTheSavedUnreadableLine()
    {
        var scene = new Shown(SourceValidatedWithUnreadable());
        await scene.OpenAsync();
        var difference = scene.Page.Difference;
        Assert.Contains("Stage sita", difference.UnreadableEvidenceText);

        scene.Fixture.WordDetailKept = false;
        scene.Cards[0] = scene.Cards[0] with { Group = ProposalGroup.SetAside, Status = "withdrawn" };
        await scene.Context.PublishBaselineCapturedAsync(openFirstRunSetup: false);

        Assert.Empty(difference.Words);
        Assert.False(difference.OffersMake);
        Assert.True(difference.HasReferenceNote);
        Assert.Equal(MotifAppStrings.ProposalDifferenceAdviceSourceValidated, difference.AdviceText);
        Assert.Equal(MotifAppStrings.ProposalDifferenceEvidenceUnreadableSaved, difference.UnreadableEvidenceText);
    }

    [Fact]
    public async Task StoppingTheWorkCancelsAMakeAgainAndTheShownSourceValidatedNotesStay()
    {
        var scene = new Shown(SourceValidatedWithUnreadable());
        var token = default(CancellationToken);
        var started = new TaskCompletionSource();
        scene.Fake.OnProduceProposalDifference(async (_, _, cancellationToken) =>
        {
            token = cancellationToken;
            started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        await scene.OpenAsync();
        var difference = scene.Page.Difference;

        var run = difference.Maker.RunCommand.ExecuteAsync(null);
        await started.Task;
        await difference.StopAsync();
        await run;

        Assert.True(token.IsCancellationRequested);
        Assert.False(difference.Maker.IsActive);
        Assert.Equal(ProposalDifferenceState.Shown, difference.State);
        Assert.True(difference.HasReferenceNote);
        Assert.Equal(MotifAppStrings.ProposalDifferenceAdviceSourceValidated, difference.AdviceText);
        Assert.Contains("Stage sita", difference.UnreadableEvidenceText);
    }

    [Fact]
    public async Task AProposalDecidedWhileADifferenceIsBeingMadeDoesNotReadAgainUnderTheRun()
    {
        var scene = new Running();
        await scene.OpenAsync();
        var run = await scene.StartAsync();
        var difference = scene.Page.Difference;
        var reads = scene.Fake.ProposalDifferenceRequests.Count;

        scene.Page.Difference.SetOffer(DifferenceOffer.Decided);
        scene.Context.TrialRun.States = scene.Context.TrialRun.States.SetItem("p/two", TrialRunState.Idle("p/two"));

        scene.AssertStillRunning();
        Assert.Equal(reads, scene.Fake.ProposalDifferenceRequests.Count);
        Assert.Equal(ProposalDifferenceState.Absent, difference.State);
        await scene.FinishAsync(run);
    }

    [Fact]
    public async Task TheTextListFollowsAReloadOfTheTextsThatKeepsTheirNumber()
    {
        var scene = new Shown();
        await scene.OpenAsync();
        Assert.Equal(GenesisId, Assert.Single(scene.Page.Difference.TextChoices).Id);

        scene.Fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(ExodusId, "Exodus 1")],
            HasBaseline: true));
        await scene.Context.Selection.LoadTextsAsync(Project);
        await scene.Context.PublishBaselineCapturedAsync(openFirstRunSetup: false);

        Assert.Equal(ExodusId, Assert.Single(scene.Page.Difference.TextChoices).Id);
        Assert.Equal(ExodusId, scene.Page.Difference.SelectedText!.Id);
    }
}
