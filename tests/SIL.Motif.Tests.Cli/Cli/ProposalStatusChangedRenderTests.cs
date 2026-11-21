using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Trait("MotifTestLevel", "Unit")]
public sealed class ProposalStatusChangedRenderTests
{
    [Theory]
    [InlineData("withdrawn")]
    [InlineData("declined")]
    public void AStatusChangeRendersAsTextThroughTheCommandRenderer(string status)
    {
        var outcome = CommandOutcome<ProposalStatusChangedResponse>.Success(
            new ProposalStatusChangedResponse("proposal/abc", status, null));

        var text = CommandTextRenderer.Render(outcome, asJson: false).Output;

        Assert.Equal($"Proposal proposal/abc is now '{status}'.{Environment.NewLine}", text);
    }

    [Fact]
    public void QueuedRunningAndFailedJobsAreNamedSeparately()
    {
        var outcome = CommandOutcome<ProposalStatusChangedResponse>.Success(new ProposalStatusChangedResponse(
            "proposal/abc", "withdrawn", null, ["q1"], ["r1"], ["f1"], "f1: the store is busy."));

        var text = CommandTextRenderer.Render(outcome, asJson: false).Output;

        Assert.Contains("Cancelled its queued job(s): q1.", text, StringComparison.Ordinal);
        Assert.Contains("Asked its running job(s) to stop: r1;", text, StringComparison.Ordinal);
        Assert.Contains("Could not cancel its job(s): f1.", text, StringComparison.Ordinal);
        Assert.Contains("warning: f1: the store is busy.", text, StringComparison.Ordinal);
    }
}
