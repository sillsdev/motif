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
            new UnreadableProposal("proposal/ccc", "proposal/ccc", "Not JSON.", "plural-draft"),
            new UnreadableProposal("proposal/ddd", "Dative", "Worse.", "dative-draft"),
        ],
    };

    [Fact]
    public void TheTextNamesEachUnreadableProposalByIdWithItsReasonAndSaysNothingWasChanged()
    {
        var text = CommandTextRenderer.Render(CommandOutcome<ProposalStandings>.Success(Standings), asJson: false)
            .Output;

        Assert.Contains("4 Proposals could not be read, so they are not listed. Nothing was changed or deleted.", text,
            StringComparison.Ordinal);
        Assert.Contains("proposal/aaa (Plural fix): Unknown operation kind 'x'.", text, StringComparison.Ordinal);
        Assert.Contains("  proposal/bbb: Bad JSON.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("proposal/bbb (proposal/bbb)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTextNamesTheDraftWhenOneIsKnown()
    {
        var text = CommandTextRenderer.Render(CommandOutcome<ProposalStandings>.Success(Standings), asJson: false)
            .Output;

        Assert.Contains("  proposal/ccc (draft plural-draft): Not JSON.", text, StringComparison.Ordinal);
        Assert.Contains("  proposal/ddd (Dative, draft dative-draft): Worse.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheJsonCarriesTheSameListAndAReadableStandingsHasNone()
    {
        var json = CommandTextRenderer.Render(CommandOutcome<ProposalStandings>.Success(Standings), asJson: true).Output;

        var rendered = ProjectionJson.Deserialize<ProposalStandings>(json)!;
        Assert.Equal(["proposal/aaa", "proposal/bbb", "proposal/ccc", "proposal/ddd"],
            rendered.Unreadable.Select(item => item.ProposalId));
        Assert.Equal("Bad JSON.", rendered.Unreadable[1].Reason);
        Assert.Null(rendered.Unreadable[0].DraftName);
        Assert.Equal("plural-draft", rendered.Unreadable[2].DraftName);
        Assert.DoesNotContain("could not be read", CommandTextRenderer.Render(
            CommandOutcome<ProposalStandings>.Success(new ProposalStandings([], 0)), asJson: false).Output,
            StringComparison.Ordinal);
    }
}
