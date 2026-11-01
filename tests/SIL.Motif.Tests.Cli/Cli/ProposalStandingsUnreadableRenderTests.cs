using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Trait("MotifTestLevel", "Unit")]
public sealed class ProposalStandingsUnreadableRenderTests
{
    private static readonly ProposalStandings Standings = new([], 0)
    {
        Unreadable =
        [
            new UnreadableProposal("proposal/aaa", "Plural fix", "Unknown operation kind 'x'."),
            new UnreadableProposal("proposal/bbb", "proposal/bbb", "Bad JSON."),
        ],
    };

    [Fact]
    public void TheTextNamesEachUnreadableProposalByIdWithItsReasonAndSaysNothingWasChanged()
    {
        var text = CommandTextRenderer.Render(CommandOutcome<ProposalStandings>.Success(Standings), asJson: false)
            .Output;

        Assert.Contains("2 Proposals could not be read, so they are not listed. Nothing was changed or deleted.", text,
            StringComparison.Ordinal);
        Assert.Contains("proposal/aaa (Plural fix): Unknown operation kind 'x'.", text, StringComparison.Ordinal);
        Assert.Contains("proposal/bbb (proposal/bbb): Bad JSON.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheJsonCarriesTheSameListAndAReadableStandingsHasNone()
    {
        var json = CommandTextRenderer.Render(CommandOutcome<ProposalStandings>.Success(Standings), asJson: true).Output;

        var rendered = ProjectionJson.Deserialize<ProposalStandings>(json)!;
        Assert.Equal(["proposal/aaa", "proposal/bbb"], rendered.Unreadable.Select(item => item.ProposalId));
        Assert.Equal("Bad JSON.", rendered.Unreadable[1].Reason);
        Assert.DoesNotContain("could not be read", CommandTextRenderer.Render(
            CommandOutcome<ProposalStandings>.Success(new ProposalStandings([], 0)), asJson: false).Output,
            StringComparison.Ordinal);
    }
}
