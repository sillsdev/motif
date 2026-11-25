namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>The saved user preferences could not be used.</summary>
    public const string PreferencesRefused = "preferences.refused";
    /// <summary>Saving user preferences failed, so choices last only until Motif closes.</summary>
    public const string PreferencesUnavailable = "preferences.unavailable";
}
