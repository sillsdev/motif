namespace SIL.Motif.App.ViewModels;

/// <summary>One Parsimony group of a Proposal: its own state, a note, and one line of counts for each measure.</summary>
/// <param name="Title">The group's name.</param>
/// <param name="StateLabel">Where this group stands, in the window's words.</param>
/// <param name="Note">A sentence about the group, or empty.</param>
/// <param name="Measures">One line per measure; counts are never added across measures.</param>
public sealed record ProposalParsimonyGroupViewModel(
    string Title, string StateLabel, string Note, IReadOnlyList<string> Measures)
{
    /// <summary>Whether the group has a note to show.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>Whether the group has per-measure lines to fold open.</summary>
    public bool HasMeasures => Measures.Count > 0;

    /// <summary>The measure lines as one block of text, a line for each measure.</summary>
    public string MeasuresText => string.Join("\n", Measures);

    /// <summary>The note above the counts, which says they are never added up.</summary>
    public string CountsNote => MotifAppStrings.ProposalParsimonyCountsNote;

    /// <summary>The group's state and name as one accessible name.</summary>
    public string AccessibleName => $"{Title}, {StateLabel}";
}
