using SIL.Motif.Commands;

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
        var label = ProposalCommands.Label(fwDataPath, ProductVersion, draftName, shortDescription);
        if (label.ExitCode != 0) throw new InvalidOperationException(label.Output);

        var comment = ProposalCommands.Comment(fwDataPath, ProductVersion, draftName, extendedExplanation);
        if (comment.ExitCode != 0) throw new InvalidOperationException(comment.Output);
    }
}
