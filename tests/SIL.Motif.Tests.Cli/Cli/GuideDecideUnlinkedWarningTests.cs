using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>The text <c>guide decide</c> prints when the section links none of the objects the Proposal changes.</summary>
public sealed class GuideDecideUnlinkedWarningTests
{
    private static readonly GdrUnlinkedObject[] Objects = [new("wordform/abc", ["wordform/incorrect-spelling"])];

    [Fact]
    public void TheSingleFormNamesTheObjectsAndHowManyMoreThereAre()
    {
        var response = new GdrEditResponse("pending", "gdr/1", "GDR-2", ["discussion"], 1, [])
        {
            UnlinkedProposalObjects = Objects, UnlinkedProposalObjectCount = 3,
        };

        var rendered = CommandTextRenderer.Render(CommandOutcome<GdrEditResponse>.Success(response), asJson: false);

        Assert.Contains("warning: GDR-2 links none of the 3 object(s) this Proposal changes", rendered.Output, StringComparison.Ordinal);
        Assert.Contains("wordform/abc (wordform/incorrect-spelling); and 2 more", rendered.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ASectionThatLinksOneGetsNoWarningAndABatchWarnsPerRow()
    {
        var quiet = CommandTextRenderer.Render(CommandOutcome<GdrEditResponse>.Success(
            new GdrEditResponse("pending", "gdr/1", "GDR-2", ["discussion"], 1, [])), asJson: false);
        Assert.DoesNotContain("links none", quiet.Output, StringComparison.Ordinal);

        var batch = new GdrDecideBatchResponse("pending", true,
            [new GdrDecidedRow(1, "GDR-2", "- a") { UnlinkedProposalObjects = Objects, UnlinkedProposalObjectCount = 1 }, new GdrDecidedRow(2, "GDR-W", "- b")],
            2, []);
        var rendered = CommandTextRenderer.Render(CommandOutcome<GdrDecideBatchResponse>.Success(batch), asJson: false);

        Assert.Contains("warning: GDR-2 links none", rendered.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("GDR-W links none", rendered.Output, StringComparison.Ordinal);
    }
}
