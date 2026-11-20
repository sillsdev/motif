using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>A cancelled grammar check says who cancelled it, so a Trial's "not checked" reason is actionable.</summary>
public sealed class GrammarCheckCancellationTests
{
    private sealed class CancellingInvoker(bool honourToken) : IPanGlossInvoker
    {
        public async Task<PanGlossOutcome> RunAsync(PanGlossRequest request, string label, CancellationToken cancellationToken,
            TimeSpan? wallClockCap = null)
        {
            if (honourToken)
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { }
            return new PanGlossOutcome.Cancelled();
        }
    }

    private static Task<(SIL.Motif.Contract.Responses.GrammarCheckResponse? Candidate, SIL.Motif.Contract.Responses.DroppedGrammar Result)>
        Check(IPanGlossInvoker invoker, TimeSpan bound, CancellationToken token) =>
        DroppedGrammarCheck.CheckAsync(
            (path, name, wait, stage) => TrialGrammarCheck.RunAsync(invoker, "label", StoredAnalysisComparison.Budget, path, name, stage, wait),
            null!, null!, "baseline.fwdata", "candidate.fwdata", "project", null!, null, bound, token);

    [Fact]
    public async Task ACheckStoppedByTheStageTimeLimitNamesTheLimitAndThatParsedWordsAreUnaffected()
    {
        var (_, result) = await Check(new CancellingInvoker(honourToken: true), TimeSpan.FromMilliseconds(200), CancellationToken.None);

        Assert.False(result.Checked);
        Assert.Contains("stage time limit", result.NotCheckedReason, StringComparison.Ordinal);
        Assert.Contains("waiting for parser capacity", result.NotCheckedReason, StringComparison.Ordinal);
        Assert.Contains("parsed are unaffected", result.NotCheckedReason, StringComparison.Ordinal);
        Assert.DoesNotContain("The parser run was cancelled", result.NotCheckedReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACheckCancelledByTheParserQueueClosingNamesTheQueueNotTheTimeLimit()
    {
        var (_, result) = await Check(new CancellingInvoker(honourToken: false), TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.False(result.Checked);
        Assert.Contains("parser queue closing", result.NotCheckedReason, StringComparison.Ordinal);
        Assert.Contains("worker was stopping or restarting", result.NotCheckedReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AJobCancelStopsTheCheckInsteadOfRecordingAnUncheckedGrammar()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Check(new CancellingInvoker(honourToken: true), TimeSpan.FromMinutes(5), cancel.Token));
    }
}
