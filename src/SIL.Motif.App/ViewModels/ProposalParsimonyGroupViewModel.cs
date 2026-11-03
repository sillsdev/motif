namespace SIL.Motif.App.ViewModels;

/// <summary>
/// One Parsimony group of a Proposal: its own state, a note, one line of counts for each measure, and once the
/// stored Reports are read, the Findings the Proposal adds or changes.
/// </summary>
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

    /// <summary>The Findings the Proposal adds or changes, each with its description; empty until the Reports are read.</summary>
    public IReadOnlyList<ProposalParsimonyFindingViewModel> Findings { get; init; } = [];

    /// <summary>What could not be read or compared, or that there is nothing to list; empty when Findings are listed.</summary>
    public string FindingsNote { get; init; } = string.Empty;

    /// <summary>One line for each measure whose earlier decisions hide, return or cannot settle a Finding.</summary>
    public IReadOnlyList<string> Judgments { get; init; } = [];

    /// <summary>Which measure each Finding the list leaves out belongs to, a sentence for each measure.</summary>
    public IReadOnlyList<string> FindingsMoreLines { get; init; } = [];

    /// <summary>How many Findings the group has beyond the ones listed.</summary>
    public int FindingsMore { get; init; }

    /// <summary>Whether the list leaves Findings out.</summary>
    public bool HasFindingsMore => FindingsMoreLines.Count > 0;

    /// <summary>The sentences that say how many Findings the list leaves out of each measure.</summary>
    public string FindingsMoreText => string.Join("\n", FindingsMoreLines);

    /// <summary>Which group this is, grammar or text, for the automation identifiers of its parts.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The Findings fold's automation identifier.</summary>
    public string FindingsAutomationId => AutomationIds.ForProposalParsimonyFindings(Key);

    /// <summary>The list of Findings' automation identifier.</summary>
    public string FindingsListAutomationId => AutomationIds.ForProposalParsimonyFindingsList(Key);

    /// <summary>The Findings note's automation identifier.</summary>
    public string FindingsNoteAutomationId => AutomationIds.ForProposalParsimonyFindingsNote(Key);

    /// <summary>The automation identifier of the lines for Findings the list leaves out.</summary>
    public string FindingsMoreAutomationId => AutomationIds.ForProposalParsimonyFindingsMore(Key);

    /// <summary>The decision lines' automation identifier.</summary>
    public string JudgmentsAutomationId => AutomationIds.ForProposalParsimonyJudgments(Key);

    /// <summary>Whether the group's stored Reports have been read, so the Findings fold has something to say.</summary>
    public bool HasFindingsRead { get; init; }

    /// <summary>The Findings fold's header, with the count in a plural sentence.</summary>
    public string FindingsHeader => PluralText.Format(MotifAppStrings.ProposalParsimonyFindingsHeaderPattern, Findings.Count + FindingsMore);

    /// <summary>Whether the group has a note about its Findings.</summary>
    public bool HasFindingsNote => FindingsNote.Length > 0;

    /// <summary>The decision lines as one block of text.</summary>
    public string JudgmentsText => string.Join("\n", Judgments);

    /// <summary>Whether any measure has a decision line.</summary>
    public bool HasJudgments => Judgments.Count > 0;
}
