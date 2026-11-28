using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Trait("MotifTestLevel", "Unit")]
public sealed class GrammarReadAtRenderTests
{
    [Fact]
    public void AReadAtAProposalNamesTheTrialCopyAndNeverCallsItTheCurrentBaseline()
    {
        var response = new GrammarReadResponse(GrammarReadKind.lexical_entry, 0, 25, [])
        {
            At = "proposal AaHnUob1K5UlJwJZtEhh_w",
            AtLabel = "the grammar the latest Trial of this Proposal ran against (Trial job job-7), not the Baseline",
        };

        var text = GrammarReadTextRenderer.Render(response);

        Assert.Contains("Read at: proposal AaHnUob1K5UlJwJZtEhh_w: the grammar the latest Trial", text, StringComparison.Ordinal);
        Assert.Contains("Trial job job-7", text, StringComparison.Ordinal);
        Assert.DoesNotContain("current", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Baseline identity unavailable", text, StringComparison.Ordinal);
    }
}
