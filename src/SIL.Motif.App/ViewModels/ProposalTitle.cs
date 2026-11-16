namespace SIL.Motif.App.ViewModels;

/// <summary>Chooses the title a person reads for a Proposal or Draft, so a raw id never stands in for a name.</summary>
public static class ProposalTitle
{
    /// <summary>Whether <paramref name="label"/> says nothing a person wrote: empty, or the id itself.</summary>
    public static bool IsUnnamed(string? label, string proposalId) =>
        string.IsNullOrWhiteSpace(label) || string.Equals(label.Trim(), proposalId, StringComparison.Ordinal);

    /// <summary>The label when it is a name, otherwise the first change's sentence, otherwise Untitled.</summary>
    public static string For(string? label, string proposalId, bool isDraft, string? firstChange = null) =>
        !IsUnnamed(label, proposalId) ? label!
        : !string.IsNullOrWhiteSpace(firstChange) ? firstChange!
        : isDraft ? MotifAppStrings.ProposalUntitledDraft : MotifAppStrings.ProposalUntitled;
}
