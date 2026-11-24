using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;
using static SIL.Motif.Tests.App.ProposalCompareViewModelTests;

namespace SIL.Motif.Tests.App;

public sealed class ProposalCompareFreshnessTests
{
    [UiThreadFact]
    public async Task ApplyingOneSideMakesTheComparisonOutdatedAndSubtractsNothing()
    {
        await using var arranged = await ArrangeAsync();
        arranged.Fake.ReadTrialCompletesWith(arranged.Scenario.Trial("one"));
        arranged.Fake.PendingChangesIs(new PendingChangesSnapshot("one", "revision/one", [], []));
        var receipt = new ApplyProjection("one", false, "Saved", [], "effect",
            new AppliedLogEntrySummary("one", "now", "Person", "intent/one"));
        arranged.Fake.ApplyProposalHandler = (_, _) => Task.FromResult(CommandOutcome<ApplyPendingResult>.Success(
            ApplyPendingResult.AppliedWith(receipt, "Saved")));
        using var compare = arranged.Compare();
        Assert.True(compare.IsComparable);

        await arranged.First.PrepareApplyCommand.ExecuteAsync(null);
        Assert.Empty(arranged.Fake.ApplyProposalRequests);
        Assert.True(arranged.First.IsConfirmingApply);
        await arranged.First.ConfirmApplyCommand.ExecuteAsync(null);

        Assert.Equal("one", Assert.Single(arranged.Fake.ApplyProposalRequests).ProposalId);
        Assert.Same(receipt, arranged.First.Receipt);
        Assert.True(arranged.Second.IsOutdated);
        Assert.False(arranged.Second.CanApply);
        Assert.Equal(ProposalComparisonState.Outdated, compare.State);
        Assert.Equal(MotifAppStrings.ProposalCompareOutdatedTitle, compare.NoticeTitle);
        Assert.Empty(compare.Rows);
        Assert.Equal(string.Empty, compare.BasisText);
        Assert.False(compare.First.ShowsEffects);
        Assert.Empty(arranged.Fake.MeasurePendingRequests);
        Assert.Empty(arranged.Fake.ProduceProposalDifferenceRequests);
    }

    [UiThreadFact]
    public async Task AChangedSideIsOutdatedUntilAPersonChoosesToMeasureAgainAndNothingMeasuresOnItsOwn()
    {
        await using var arranged = await ArrangeAsync();
        using var compare = arranged.Compare();
        arranged.Second.InvalidateEvidence();

        Assert.Equal(ProposalComparisonState.Outdated, compare.State);
        Assert.Equal(ProposalComparisonReason.Outdated, compare.Reason);
        Assert.Empty(compare.Rows);
        Assert.True(compare.CanRemeasure);
        var reads = arranged.Fake.ProposalDifferenceRequests.Count;
        await Task.Delay(50);
        Assert.Equal(reads, arranged.Fake.ProposalDifferenceRequests.Count);
        Assert.Empty(arranged.TriedAgain);

        await compare.RemeasureCommand.ExecuteAsync(null);

        Assert.Equal(ProposalComparisonState.Comparable, compare.State);
        Assert.Equal(["fills", "other"], compare.Rows.Select(row => row.Word).Order());
        Assert.False(compare.IsRemeasuring);
        Assert.Empty(arranged.TriedAgain);
        Assert.Empty(arranged.Fake.ProduceProposalDifferenceRequests);
    }

    [UiThreadFact]
    public async Task OnlyTheSideThatNeedsItIsMeasuredAgain()
    {
        await using var arranged = await ArrangeAsync();
        using var compare = arranged.Compare();
        arranged.Second.InvalidateEvidence();
        var firstReads = arranged.Fake.ProposalDifferenceRequests.Count(request => request.ProposalId == "one");

        await compare.RemeasureCommand.ExecuteAsync(null);

        Assert.Equal(firstReads, arranged.Fake.ProposalDifferenceRequests.Count(request => request.ProposalId == "one"));
    }

    [UiThreadFact]
    public async Task ALateReadForASupersededGenerationNeverMakesTheSideCurrent()
    {
        await using var arranged = await ArrangeAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        arranged.Scenario.BeforeRead = async (id, _) =>
        {
            if (id != "two") return;
            held.TrySetResult();
            await release.Task;
        };
        using var compare = arranged.Compare();
        Assert.True(compare.IsComparable);

        var reading = arranged.Second.RefreshAsync();
        await held.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ProposalComparisonState.Reading, compare.State);
        Assert.Empty(compare.Rows);
        Assert.Equal(string.Empty, compare.BasisText);
        arranged.Second.InvalidateEvidence();
        release.SetResult();
        await reading;

        Assert.Equal(ProposalComparisonState.Outdated, compare.State);
        Assert.Empty(compare.Rows);
        Assert.False(compare.First.ShowsEffects);
    }

    [UiThreadFact]
    public async Task ADifferentTrialOrIntentOnASideIsOutdatedNotComparable()
    {
        await using var arranged = await ArrangeAsync(scenario => scenario
            .Replace("two", summary => summary with { TrialJobId = "job/another" }));
        using var compare = arranged.Compare();

        Assert.Equal(ProposalComparisonState.Outdated, compare.State);
        Assert.Empty(compare.Rows);
        Assert.True(compare.CanRemeasure);
    }

    [UiThreadFact]
    public async Task ChangedWorkspaceEvidenceMakesTheComparisonOutdated()
    {
        await using var arranged = await ArrangeAsync();
        using var compare = arranged.Compare();
        Assert.True(compare.IsComparable);
        arranged.Context.Evidence.AppliedSinceRefresh = true;

        Assert.True(arranged.Context.Evidence.IsStale);
        Assert.Equal(ProposalComparisonState.Outdated, compare.State);
        Assert.Empty(compare.Rows);
    }

    [UiThreadFact]
    public async Task LeavingTheComparisonStopsAMeasureAgainThatIsStillReading()
    {
        await using var arranged = await ArrangeAsync();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        arranged.Scenario.BeforeRead = async (id, token) =>
        {
            if (id != "two") return;
            held.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelled = true; throw; }
        };
        using var compare = arranged.Compare();
        arranged.Second.InvalidateEvidence();

        var remeasuring = compare.RemeasureCommand.ExecuteAsync(null);
        await held.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(compare.IsRemeasuring);
        compare.BackCommand.Execute(null);
        await remeasuring.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(cancelled);
        Assert.False(compare.IsRemeasuring);
        Assert.Equal(1, arranged.Backs);
    }

    [UiThreadFact]
    public async Task DisposingTheComparisonStopsAMeasureAgainThatIsStillReading()
    {
        await using var arranged = await ArrangeAsync();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        arranged.Scenario.BeforeRead = async (id, token) =>
        {
            if (id != "two") return;
            held.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var compare = arranged.Compare();
        arranged.Second.InvalidateEvidence();

        var remeasuring = compare.RemeasureCommand.ExecuteAsync(null);
        await held.Task.WaitAsync(TimeSpan.FromSeconds(10));
        compare.Dispose();
        await remeasuring.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(compare.IsRemeasuring);
    }
}
