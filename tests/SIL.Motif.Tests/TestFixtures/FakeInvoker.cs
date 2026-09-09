using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Stands in for the invocation module without a process: records every request and answers with whatever
/// <see cref="Respond"/> says, completing by default.
/// </summary>
internal sealed class FakeInvoker : IPanGlossInvoker
{
    public List<(PanGlossRequest Request, string Label)> Requests { get; } = new();

    /// <summary>What to answer; may write files a request promises (a batch cache, an import's grammar).</summary>
    public Func<PanGlossRequest, PanGlossOutcome> Respond { get; set; } =
        _ => new PanGlossOutcome.Completed(string.Empty, string.Empty, TimeSpan.Zero);

    public Task<PanGlossOutcome> RunAsync(
        PanGlossRequest request, string label, CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
    {
        Requests.Add((request, label));
        if (cancellationToken.IsCancellationRequested) return Task.FromResult<PanGlossOutcome>(new PanGlossOutcome.Cancelled());
        return Task.FromResult(Respond(request));
    }
}
