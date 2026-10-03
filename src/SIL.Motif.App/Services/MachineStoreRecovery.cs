namespace SIL.Motif.App.Services;

/// <summary>Local recovery text for a machine store problem.</summary>
internal static class MachineStoreRecovery
{
    /// <summary>Explains where the refused file is and how to preserve it before manual recovery.</summary>
    public static string ForRefusedStore(string managedRoot)
    {
        var storePath = Path.GetFullPath(Path.Combine(managedRoot, "motif.db"));
        return "Motif could not read its machine store at " + storePath + ". Close every Motif window. " +
            "Keep a copy for support, then move or remove this file only if you can confirm it is Motif's " +
            "machine store; reopen Motif to create a fresh one. Known projects and local usage history will " +
            "be cleared. Your FieldWorks projects and their pending changes are not changed. If you cannot " +
            "confirm that this is Motif's file, leave it in place and ask for help.";
    }
}
