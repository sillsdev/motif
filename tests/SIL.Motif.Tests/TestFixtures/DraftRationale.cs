using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Authors both required rationale fields through the same CLI commands production callers use.</summary>
internal static class DraftRationale
{
    private const string ProductVersion = "1.0";

    /// <summary>Authors both required rationale fields for a Proposal fixture.</summary>
    public static void Author(
        string fwDataPath,
        string draftName,
        string shortDescription,
        string extendedExplanation)
    {
        var label = ProposalCommands.Label(new LabelRequest(fwDataPath, ProductVersion, draftName, shortDescription));
        if (!label.Succeeded) throw new InvalidOperationException(label.Refusal!.Message);

        var comment = ProposalCommands.Comment(
            new CommentRequest(fwDataPath, ProductVersion, draftName, extendedExplanation));
        if (!comment.Succeeded) throw new InvalidOperationException(comment.Refusal!.Message);
    }
}
