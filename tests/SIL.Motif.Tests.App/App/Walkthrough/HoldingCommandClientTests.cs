using SIL.Motif.Commands.Handoff;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

public sealed class HoldingCommandClientTests
{
    [Fact]
    public async Task HeldHandoffForwardsCancellationToInnerOnlyAfterRelease()
    {
        var inner = new FakeCommandClient();
        var refusal = new Refusal(
            "handoff.cancelled", FailureReason.Cancelled, "The Handoff run was cancelled.");
        inner.HandoffBlocksUntilCancelled(refusal);
        var holding = new HoldingCommandClient(inner, holdHandoff: true);
        using var cancellation = new CancellationTokenSource();
        var request = new HandoffRequest(
            "project.fwdata", "handoff", new SelectionRequest(false, [], [], false, null), false);

        var running = holding.HandoffAsync(request, new Progress<AssessmentProgress>(), cancellation.Token);
        await Task.Yield();
        Assert.Empty(inner.HandoffRequests);

        cancellation.Cancel();
        Assert.Empty(inner.HandoffRequests);

        holding.ReleaseHandoff();
        var outcome = await running;

        Assert.False(outcome.Succeeded);
        Assert.Equal(refusal, outcome.Refusal);
        Assert.Single(inner.HandoffRequests);
    }
}
