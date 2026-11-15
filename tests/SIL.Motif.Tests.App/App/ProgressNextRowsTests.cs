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
        var refresh = context.PublishRequestsChangedAsync();
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
        var first = context.PublishRequestsChangedAsync();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([], 0));
        try
        {
            await context.PublishRequestsChangedAsync();
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
    public async Task NextListsWaitingProposalsAndOpenRequestsWithoutInventingAQuestion()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([WaitingCard(),
            WaitingCard() with { ProposalId = "proposal/other", Group = ProposalGroup.NeedsAnotherTry }], 0));
        fake.ProgressRequestCountHandler = (_, _) => Task.FromResult(CommandOutcome<ProgressRequestCount>.Success(new ProgressRequestCount(2_345)));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        try
        {
            await context.OpenProjectAsync("project.fwdata");
            Assert.Equal(1, page.WaitingProposals);
            Assert.Equal(2_345, page.OpenRequests);
            Assert.Equal(2, page.NextRows.Count);
            Assert.Equal("Proposals", page.NextRows[0].Category);
            Assert.Contains("waiting for your decision", page.NextRows[0].Text);
            Assert.Equal("Requests", page.NextRows[1].Category);
            Assert.Contains("awaiting an answer", page.NextRows[1].Text);
            Assert.DoesNotContain("Claude", page.NextRows[1].Text);
            for (var index = 0; index < 2; index++)
            {
                context.OpenPage(WorkspacePage.Progress);
                page.NextRows[index].OpenCommand.Execute(null);
                Assert.Equal(WorkspacePage.Proposals, context.CurrentPage);
            }
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
        fake.ProgressRequestCountHandler = (_, _) => Task.FromResult(CommandOutcome<ProgressRequestCount>.Refused(
            new Refusal("requests.unavailable", FailureReason.Refused, "Unavailable")));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        try
        {
            await context.OpenProjectAsync("project.fwdata");
            Assert.True(page.HasUnavailableNext);
            Assert.Null(page.WaitingProposals);
            Assert.Null(page.OpenRequests);
            Assert.Empty(page.NextRows);
            fake.ProposalStandingsCompleteWith(new ProposalStandings([WaitingCard()], 0));
            fake.ProgressRequestCountHandler = (_, _) => Task.FromResult(CommandOutcome<ProgressRequestCount>.Success(new ProgressRequestCount(0)));
            await page.TryAgainCommand.ExecuteAsync(null);
            Assert.False(page.HasUnavailableNext);
            Assert.Equal(0, page.OpenRequests);
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
            Assert.Equal(0, page.OpenRequests);
            Assert.False(page.HasNextSection);
            Assert.False(page.HasUnavailableNext);
        }
        finally { await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task AContextChangeDiscardsLateTotalsAndDisablesRetainedRows()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([WaitingCard()], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        await context.EvidencePublication;
        var oldRow = Assert.Single(page.NextRows);
        var pending = new TaskCompletionSource<CommandOutcome<ProgressRequestCount>>();
        CancellationToken oldToken = default;
        fake.ProgressRequestCountHandler = (_, token) => { oldToken = token; return pending.Task; };
        var oldRead = context.PublishRequestsChangedAsync();
        fake.ProgressRequestCountHandler = (_, _) => Task.FromResult(CommandOutcome<ProgressRequestCount>.Success(new ProgressRequestCount(0)));
        fake.ProposalStandingsCompleteWith(new ProposalStandings([], 0));
        try
        {
            context.Selection.PastedWords = "different-context";
            await context.EvidencePublication;
            Assert.True(oldToken.IsCancellationRequested);
            Assert.False(oldRow.OpenCommand.CanExecute(null));
            oldRow.OpenCommand.Execute(null);
            Assert.Equal(WorkspacePage.Progress, context.CurrentPage);
            pending.SetResult(CommandOutcome<ProgressRequestCount>.Success(new ProgressRequestCount(999)));
            await oldRead;
            Assert.Equal(0, page.OpenRequests);
            Assert.Empty(page.NextRows);
        }
        finally
        {
            pending.TrySetResult(CommandOutcome<ProgressRequestCount>.Success(new ProgressRequestCount(999)));
            await oldRead;
            await context.StopProjectWorkAsync();
        }
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
        var pending = new TaskCompletionSource<CommandOutcome<ProgressRequestCount>>();
        CancellationToken oldToken = default;
        fake.ProgressRequestCountHandler = (_, token) => { oldToken = token; return pending.Task; };
        var oldRead = context.PublishRequestsChangedAsync();
        fake.ProgressRequestCountHandler = (_, _) => Task.FromResult(
            CommandOutcome<ProgressRequestCount>.Success(new ProgressRequestCount(2)));
        var switching = context.OpenProjectAsync("second.fwdata");
        try
        {
            Assert.True(oldToken.IsCancellationRequested);
            Assert.Empty(page.NextRows);
            Assert.False(switching.IsCompleted);
            Assert.False(oldRow.OpenCommand.CanExecute(null));
            pending.SetResult(CommandOutcome<ProgressRequestCount>.Success(new ProgressRequestCount(999)));
            await oldRead;
            await switching;
            Assert.Equal("second.fwdata", context.ProjectPath);
            Assert.Equal(2, page.OpenRequests);
            Assert.Equal(2, page.NextRows.Count);
            oldRow.OpenCommand.Execute(null);
            Assert.Equal(WorkspacePage.Progress, context.CurrentPage);
        }
        finally
        {
            pending.TrySetResult(CommandOutcome<ProgressRequestCount>.Success(new ProgressRequestCount(999)));
            await oldRead;
            await switching;
            await context.StopProjectWorkAsync();
        }
    }

    private static ProposalCard WaitingCard() => new("proposal/1", "First", "proposed", ProposalGroup.WaitingForYou,
        false, [], TrialStanding.Current, null, []);

}
