namespace SIL.Motif.App.ViewModels;

public enum PendingChangeState
{
    None,
    NotAppliedYet,
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
