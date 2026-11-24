using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProgressPageModelTests
{
    public ProgressPageModelTests() => AvaloniaHeadlessPlatform.Initialize();

    [Fact]
    public void SelectionSummaryRequiresAResolvedCurrentOverview()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        var health = context.Health;
        var overview = SampleOverview.Populated();
        health.Overview = overview with { SelectionResolved = false };
        Assert.False(health.HasSelectionSummary);
        Assert.Equal(string.Empty, health.SelectionSummary);

        health.Overview = overview;
        Assert.True(health.HasSelectionSummary);
        Assert.StartsWith("Default Selection:", health.SelectionSummary);
        Assert.Contains("2 texts", health.SelectionSummary);
        Assert.Contains("142 words", health.SelectionSummary);

        health.Overview = overview with { IsStale = true };
        Assert.False(health.HasSelectionSummary);
        Assert.Equal(string.Empty, health.SelectionSummary);
    }

    [UiThreadFact]
    public async Task StoredHealthAppearsWhileProposalStandingsAreStillLoading()
    {
        var fake = new FakeCommandClient();
        var overview = SampleOverview.Populated();
        fake.OverviewCompletesWith(overview);
        var entered = new TaskCompletionSource();
        var pending = new TaskCompletionSource<CommandOutcome<ProposalStandings>>();
        fake.OnProposalStandings((_, _) =>
        {
            entered.TrySetResult();
            return pending.Task;
        });
        var context = WorkspaceContextTests.NewContext(fake);
        _ = new ProgressPageModel(context);

        var opening = context.OpenProjectAsync("project.fwdata");
        await entered.Task;
        Assert.Same(overview, context.Health.Overview);
        pending.SetResult(CommandOutcome<ProposalStandings>.Success(new ProposalStandings([], 0)));
        await opening;
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task ComingBackToProgressWaitsForTheReadAlreadyRunningInsteadOfStartingOver()
    {
        var fake = new FakeCommandClient();
        fake.OverviewCompletesWith(SampleOverview.Populated());
        var pending = new TaskCompletionSource<CommandOutcome<ProposalStandings>>();
        var standingsRequests = 0;
        fake.OnProposalStandings((_, _) =>
        {
            Interlocked.Increment(ref standingsRequests);
            return pending.Task;
        });
        var context = WorkspaceContextTests.NewContext(fake);
        var model = new ProgressPageModel(context);
        context.CurrentPage = WorkspacePage.Texts;

        var opening = context.OpenProjectAsync("project.fwdata");
        Assert.True(model.IsReading);
        Assert.Equal(1, standingsRequests);

        context.CurrentPage = WorkspacePage.Progress;
        context.CurrentPage = WorkspacePage.Texts;
        context.CurrentPage = WorkspacePage.Progress;
        Assert.True(model.IsReading);
        Assert.Equal(1, standingsRequests);
        Assert.Single(fake.OverviewRequests);

        pending.SetResult(CommandOutcome<ProposalStandings>.Success(new ProposalStandings([], 0)));
        await opening;
        Assert.False(model.IsReading);
        Assert.Equal(1, standingsRequests);
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task ReplacingEvidenceHidesTheOldHealthWhileItsReplacementIsRead()
    {
        var fake = new FakeCommandClient();
        fake.OverviewCompletesWith(SampleOverview.Populated());
        var context = WorkspaceContextTests.NewContext(fake);
        _ = new ProgressPageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        Assert.NotNull(context.Health.Overview);
        await context.EvidencePublication;
        var entered = new TaskCompletionSource();
        var pending = new TaskCompletionSource<CommandOutcome<OverviewResponse>>();
        fake.OnOverview((_, _) => { entered.TrySetResult(); return pending.Task; });

        context.PublishEvidence(new WorkspaceEvidence(WorkspaceContextTests.Assessment(), DateTimeOffset.Now, false));

        Assert.Null(context.Health.Overview);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        pending.SetResult(CommandOutcome<OverviewResponse>.Success(SampleOverview.Populated()));
        await context.EvidencePublication;
        Assert.NotNull(context.Health.Overview);
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task NextOnlyShowsProposalsActuallyWaitingForADecision()
    {
        var fake = new FakeCommandClient();
        fake.OverviewCompletesWith(SampleOverview.Populated());
        var waiting = new ProposalCard("proposal/1", "First", "proposed", ProposalGroup.WaitingForYou, false, [],
            TrialStanding.Current, null, []);
        var draft = waiting with { ProposalId = "proposal/2", Group = ProposalGroup.NotTried, IsDraft = true };
        fake.ProposalStandingsCompleteWith(new ProposalStandings([waiting, draft], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);

        await context.OpenProjectAsync("project.fwdata");

        Assert.True(page.HasWaitingProposals);
        Assert.Equal(1, page.WaitingProposals);
        Assert.NotEmpty(fake.OverviewRequests);
        Assert.Empty(fake.AssessRequests);
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task PartialStandingsDoNotPresentACompleteWaitingCount()
    {
        var fake = new FakeCommandClient();
        var waiting = new ProposalCard("proposal/1", "First", "proposed", ProposalGroup.WaitingForYou, false, [],
            TrialStanding.Current, null, []);
        fake.ProposalStandingsCompleteWith(new ProposalStandings([waiting], 0)
        {
            Unreadable = [new UnreadableProposal("proposal/2", "Unreadable", "Unavailable")],
        });
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);

        await context.OpenProjectAsync("project.fwdata");

        Assert.Null(page.WaitingProposals);
        Assert.False(page.HasWaitingProposals);
        await context.StopProjectWorkAsync();
    }
}
