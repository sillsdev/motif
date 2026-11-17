using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProgressNextRowsTests
{
    public ProgressNextRowsTests() => AvaloniaHeadlessPlatform.Initialize();

    [Fact]
    public async Task DeferredProposalsAreNotCountedAsWaiting()
    {
        var fake = new FakeCommandClient();
        ProposalCard Card(string id, string status) => new(id, id, status, ProposalGroup.WaitingForYou, false, [],
            TrialStanding.Current, null, []);
        fake.ProposalStandingsCompleteWith(new ProposalStandings(
            [Card("proposal/1", "proposed"), Card("proposal/2", "deferred"), Card("proposal/3", "deferred")], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        await context.EvidencePublication;
        Assert.Equal(1, page.WaitingProposals);
    }

    [Fact]
    public async Task RefreshHidesThePreviousWaitingCountBeforeTheNewReadFinishes()
    {
        var fake = new FakeCommandClient();
        var waiting = new ProposalCard("proposal/1", "First", "proposed", ProposalGroup.WaitingForYou, false, [],
            TrialStanding.Current, null, []);
        fake.ProposalStandingsCompleteWith(new ProposalStandings([waiting], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        await context.EvidencePublication;
        Assert.Equal(1, page.WaitingProposals);
        var entered = new TaskCompletionSource();
        var pending = new TaskCompletionSource<CommandOutcome<ProposalStandings>>();
        fake.OnProposalStandings((_, _) => { entered.TrySetResult(); return pending.Task; });
        var refresh = context.PublishGrammarCheckedAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(page.WaitingProposals);
            Assert.False(page.HasWaitingProposals);
        }
        finally
        {
            pending.TrySetResult(CommandOutcome<ProposalStandings>.Success(new ProposalStandings([], 0)));
            await refresh;
            await context.StopProjectWorkAsync();
        }
    }
    [Fact]
    public async Task AReplacementReadCancelsTheReadItSupersedes()
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        _ = new ProgressPageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        await context.EvidencePublication;
        var pending = new TaskCompletionSource<CommandOutcome<ProposalStandings>>();
        CancellationToken oldToken = default;
        fake.OnProposalStandings((_, token) => { oldToken = token; return pending.Task; });
        var first = context.PublishGrammarCheckedAsync();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([], 0));
        try
        {
            await context.PublishGrammarCheckedAsync();
            Assert.True(oldToken.IsCancellationRequested);
        }
        finally
        {
            pending.TrySetResult(CommandOutcome<ProposalStandings>.Success(new ProposalStandings([], 0)));
            await first;
            await context.StopProjectWorkAsync();
        }
    }

    [Fact]
    public async Task ANonDraftProposalThatNeedsAnotherTryCountsAsWaiting()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([WaitingCard(),
            WaitingCard() with { ProposalId = "proposal/other", Group = ProposalGroup.NeedsAnotherTry }], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        try
        {
            await context.OpenProjectAsync("project.fwdata");
            Assert.Equal(2, page.WaitingProposals);
        }
        finally { await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task NextListsOnlyWaitingProposalsAndNeverARequestRow()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([WaitingCard(),
            WaitingCard() with { ProposalId = "proposal/other", Group = ProposalGroup.NotTried, IsDraft = true }], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        try
        {
            await context.OpenProjectAsync("project.fwdata");
            Assert.Equal(1, page.WaitingProposals);
            var row = Assert.Single(page.NextRows);
            Assert.Equal("Proposals", row.Category);
            Assert.Contains("waiting for your decision", row.Text);
            Assert.DoesNotContain("Claude", row.Text);
            context.OpenPage(WorkspacePage.Progress);
            row.OpenCommand.Execute(null);
            Assert.Equal(WorkspacePage.Proposals, context.CurrentPage);
            Assert.Empty(fake.AssessRequests);
        }
        finally { await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task UnavailableAndUnreadableAreNotZeroAndRetryRestoresTheRows()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([WaitingCard()], 0)
        {
            Unreadable = [new UnreadableProposal("proposal/other", "Other", "Unreadable")],
        });
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        try
        {
            await context.OpenProjectAsync("project.fwdata");
            Assert.True(page.HasUnavailableNext);
            Assert.Null(page.WaitingProposals);
            Assert.Empty(page.NextRows);
            fake.ProposalStandingsCompleteWith(new ProposalStandings([WaitingCard()], 0));
            await page.TryAgainCommand.ExecuteAsync(null);
            Assert.False(page.HasUnavailableNext);
            Assert.Single(page.NextRows);
            Assert.Empty(fake.AssessRequests);
        }
        finally { await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task SuccessfulEmptyReadsLeaveNextOut()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        var page = new ProgressPageModel(context);
        try
        {
            await context.OpenProjectAsync("project.fwdata");
            Assert.Equal(0, page.WaitingProposals);
            Assert.False(page.HasNextSection);
            Assert.False(page.HasUnavailableNext);
        }
        finally { await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task AContextChangeDisablesRetainedRowsAndClearsTheTotals()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([WaitingCard()], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        await context.EvidencePublication;
        var oldRow = Assert.Single(page.NextRows);
        fake.ProposalStandingsCompleteWith(new ProposalStandings([], 0));
        try
        {
            context.Selection.PastedWords = "different-context";
            await context.EvidencePublication;
            Assert.False(oldRow.OpenCommand.CanExecute(null));
            oldRow.OpenCommand.Execute(null);
            Assert.Equal(WorkspacePage.Progress, context.CurrentPage);
            Assert.Empty(page.NextRows);
        }
        finally { await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task AppliedEvidenceImmediatelyRemovesNextAndMakesOldCommandsInert()
    {
        var fake = new FakeCommandClient();
        var overview = SampleOverview.Populated();
        fake.OverviewCompletesWith(overview);
        fake.ProposalStandingsCompleteWith(new ProposalStandings([WaitingCard()], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        try
        {
            await context.OpenProjectAsync("project.fwdata");
            await context.EvidencePublication;
            Assert.Same(overview, page.Health.Overview);
            var row = Assert.Single(page.NextRows);
            context.Evidence.AppliedSinceRefresh = true;
            Assert.Empty(page.NextRows);
            Assert.Null(page.WaitingProposals);
            Assert.Null(page.Health.Overview);
            Assert.False(page.Health.HasSelectionSummary);
            Assert.False(row.OpenCommand.CanExecute(null));
            row.OpenCommand.Execute(null);
            Assert.Equal(WorkspacePage.Progress, context.CurrentPage);
        }
        finally { await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task AStaleOverviewNeverPublishesCurrentWaitingTotals()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([WaitingCard()], 0));
        fake.OverviewCompletesWith(SampleOverview.Populated() with { IsStale = true });
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        try
        {
            await context.OpenProjectAsync("project.fwdata");
            Assert.Empty(page.NextRows);
            Assert.Null(page.WaitingProposals);
            Assert.False(page.Health.HasSelectionSummary);
            Assert.True(page.HasUnavailableNext);
        }
        finally { await context.StopProjectWorkAsync(); }
    }


    [Fact]
    public async Task ReturningToProgressReadsActionsChangedOnAnotherPage()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([WaitingCard()], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        try
        {
            await context.OpenProjectAsync("project.fwdata");
            Assert.Single(page.NextRows);
            context.OpenPage(WorkspacePage.Proposals);
            fake.ProposalStandingsCompleteWith(new ProposalStandings([], 0));
            context.OpenPage(WorkspacePage.Progress);
            Assert.Equal(0, page.WaitingProposals);
            Assert.Empty(page.NextRows);
            Assert.Empty(fake.AssessRequests);
        }
        finally { await context.StopProjectWorkAsync(); }
    }


    [Fact]
    public async Task ProjectSwitchCancelsAndJoinsTheOldReadBeforePublishingNewTotals()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([WaitingCard()], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        await context.OpenProjectAsync("first.fwdata");
        await context.EvidencePublication;
        var oldRow = Assert.Single(page.NextRows);
        var pending = new TaskCompletionSource<CommandOutcome<ProposalStandings>>();
        CancellationToken oldToken = default;
        fake.OnProposalStandings((_, token) => { oldToken = token; return pending.Task; });
        var oldRead = context.PublishGrammarCheckedAsync();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([WaitingCard()], 0));
        var switching = context.OpenProjectAsync("second.fwdata");
        try
        {
            Assert.True(oldToken.IsCancellationRequested);
            Assert.Empty(page.NextRows);
            Assert.False(switching.IsCompleted);
            Assert.False(oldRow.OpenCommand.CanExecute(null));
            pending.SetResult(CommandOutcome<ProposalStandings>.Success(new ProposalStandings([], 0)));
            await oldRead;
            await switching;
            Assert.Equal("second.fwdata", context.ProjectPath);
            Assert.Equal(1, page.WaitingProposals);
            Assert.Single(page.NextRows);
            oldRow.OpenCommand.Execute(null);
            Assert.Equal(WorkspacePage.Progress, context.CurrentPage);
        }
        finally
        {
            pending.TrySetResult(CommandOutcome<ProposalStandings>.Success(new ProposalStandings([], 0)));
            await oldRead;
            await switching;
            await context.StopProjectWorkAsync();
        }
    }

    private static ProposalCard WaitingCard() => new("proposal/1", "First", "proposed", ProposalGroup.WaitingForYou,
        false, [], TrialStanding.Current, null, []);

}
