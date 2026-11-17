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
}
