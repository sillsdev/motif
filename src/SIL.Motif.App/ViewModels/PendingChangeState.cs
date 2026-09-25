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
}

internal static class PendingChangeStates
{
    public static string? Label(PendingChangeState state) => state switch
    {
        PendingChangeState.NotAppliedYet => "Not applied yet",
        PendingChangeState.NoLongerFits => "No longer fits",
        _ => null,
    };
}
