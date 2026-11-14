using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Worker.Worker;

public sealed class TrialGrammarCheckFactsTests
{
    [Fact]
    public async Task TrialDoesNotRequestFacts()
    {
        var invoker = new CaptureInvoker();
        await Assert.ThrowsAsync<InvalidOperationException>(() => TrialGrammarCheck.RunAsync(invoker,
            "trial", StoredAnalysisComparison.Off, "project", "project", CancellationToken.None,
            snapshotPath: "snapshot"));
        Assert.False(invoker.Request!.FactsLight);
        Assert.Null(invoker.Request.FactsContextJson);
    }

    private sealed class CaptureInvoker : IPanGlossInvoker
    {
        public PanGlossRequest.GrammarHealth? Request { get; private set; }
        public Task<PanGlossOutcome> RunAsync(PanGlossRequest request, string label,
            CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
        {
            Request = (PanGlossRequest.GrammarHealth)request;
            return Task.FromResult<PanGlossOutcome>(new PanGlossOutcome.Unavailable("captured"));
        }
    }
}
