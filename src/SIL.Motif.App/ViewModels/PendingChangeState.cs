namespace SIL.Motif.App.ViewModels;

/// <summary>The shared condition of a collected change across the Texts views.</summary>
public enum PendingChangeState
{
    /// <summary>No change is waiting for this word.</summary>
    None,
    /// <summary>A change is collected but has not been applied.</summary>
    NotAppliedYet,
    /// <summary>The collected change no longer fits the current project.</summary>
    NoLongerFits,
    /// <summary>The source sentence changed and the person needs to check the decision again.</summary>
    Uncertain,
}

internal static class PendingChangeStates
{
    public static PendingChangeState FromChanges(IReadOnlyCollection<ChangeViewModel>? changes) =>
        changes?.Any(change => change.IsNoLongerFits) == true
            ? PendingChangeState.NoLongerFits
            : changes?.Any(change => change.IsUncertain) == true
                ? PendingChangeState.Uncertain
            : changes?.Count > 0 ? PendingChangeState.NotAppliedYet : PendingChangeState.None;

    public static string? Label(PendingChangeState state) => state switch
    {
        PendingChangeState.NotAppliedYet => "Not applied yet",
        PendingChangeState.NoLongerFits => "No longer fits",
        PendingChangeState.Uncertain => "Needs another look",
        _ => null,
    };
}
