using System.Windows.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// What keeps Apply from writing the pending changes, in the order a person clears them: first learn whether an
/// earlier Apply already reached FieldWorks, then settle the changes themselves, then the project, then the numbers.
/// </summary>
public enum ApplyBlockerKind
{
    /// <summary>An earlier Apply may have saved to FieldWorks without Motif confirming it; Refresh settles it.</summary>
    ReconciliationNeeded,

    /// <summary>At least one change was made against a word or analysis FieldWorks has since changed.</summary>
    NoLongerFits,

    /// <summary>At least one change's sentence changed in FieldWorks, so the person must reconfirm or undo it.</summary>
    Uncertain,

    /// <summary>FieldWorks has the project open, so it would keep its own copy over Motif's write.</summary>
    FieldWorksHoldsProject,

    /// <summary>FieldWorks saved the project after the numbers were measured.</summary>
    FieldWorksSavedSince,

    /// <summary>Applying would leave a word without an analysis a person approved.</summary>
    LosesApprovedAnalysis,

    /// <summary>Nobody has yet measured what these exact changes do to the numbers.</summary>
    NotMeasured,
}

/// <summary>
/// One reason Apply is blocked, as Review changes names it, with the action that clears it when the page has one.
/// A blocker without an action is cleared on each change's own row, or outside Motif.
/// </summary>
public sealed record ApplyBlocker(ApplyBlockerKind Kind, string Sentence, string? ActionText = null,
    ICommand? Action = null)
{
    public bool HasAction => ActionText is not null && Action is not null;
}
