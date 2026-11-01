using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Trait("MotifTestLevel", "Unit")]
public sealed class ProposalDifferenceRenderingTests
{
    private static ProposalDifferenceSummary Summary()
    {
        var pin = new ProposalDifferenceSidePin("1.0.0", "sha256:p", "sha256:g", "selection", "sha256:s", "tokeniser", "1");
        return new("difference/1", "proposal/1", "job/1", "assessment/b", "assessment/a", "text/1", "Title", "baseline/1",
            new ProposalDifferencePolicy(), 2, 3, 0, 1, 0, 0, [], [], "parsimony", null, pin, pin,
            ["The parser differs: 1.0.0 against 1.0.1."], 2, [new(HumanAgreementLimitation.Capped, 1, 2)],
            "2026-10-31T00:00:00Z");
    }

    [Fact]
    public void TheRenderingShowsWarningsOutsideTextWordsAndLimitationCounts()
    {
        var response = new ProposalDifferenceResponse(Summary(), [], 0, null, true);

        var rendered = CommandTextRenderer.Render(CommandOutcome<ProposalDifferenceResponse>.Success(response), asJson: false);

        Assert.Contains("Warning: The parser differs: 1.0.0 against 1.0.1.", rendered.Output);
        Assert.Contains("2 measured words are not in this Text", rendered.Output);
        Assert.Contains("Limit capped: 1 word, 2 occurrences", rendered.Output);
        Assert.Contains("Showing 0 of 0 matching words", rendered.Output);
    }

    [Fact]
    public void ADecidedProposalsDifferenceSaysWordDetailIsNoLongerKept()
    {
        var response = new ProposalDifferenceResponse(Summary(), [], 0, null, false);

        var rendered = CommandTextRenderer.Render(CommandOutcome<ProposalDifferenceResponse>.Success(response), asJson: false);

        Assert.Contains("Word detail is no longer kept", rendered.Output);
        Assert.DoesNotContain("Showing", rendered.Output);
    }
}
