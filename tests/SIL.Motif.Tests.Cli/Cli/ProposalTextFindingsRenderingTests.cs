using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ProposalTextFindingsRenderingTests
{
    private static readonly TextFinding Missing = new("fwmd.link.not-found", 1, 9, "'/lexicon/entry-9' matches no object of this project.");

    [Fact]
    public void ARationaleWithFindingsSaysItCannotBeFinalizedAndListsEachWithItsPlace()
    {
        var text = ProposalCommandRenderer.RenderTextChanged(
            new ProposalTextChangedResponse("p-1", "rationale", "2026-11-10T00:00:00Z", false) { Findings = [Missing] });

        Assert.Contains("cannot be finalized", text, StringComparison.Ordinal);
        Assert.Contains("1:9 fwmd.link.not-found", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACommentWithFindingsListsThemAndACleanTextSaysNothingMore()
    {
        var withFindings = ProposalCommandRenderer.RenderCommentChanged(
            new ProposalCommentChangedResponse("p-1", 3, "added", false) { Findings = [Missing] });
        var clean = ProposalCommandRenderer.RenderCommentChanged(new ProposalCommentChangedResponse("p-1", 3, "added", false));

        Assert.Contains("1:9 fwmd.link.not-found", withFindings, StringComparison.Ordinal);
        Assert.Equal($"Comment 3 added on Proposal p-1.{Environment.NewLine}", clean);
    }
}
