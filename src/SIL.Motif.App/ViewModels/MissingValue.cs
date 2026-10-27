namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The FieldWorks marker for a gloss or analysis that is missing. Empty values elsewhere stay empty: no placeholder
/// characters stand in for them.
/// </summary>
public static class MissingValue
{
    /// <summary>Shown in place of a missing gloss or analysis.</summary>
    public const string Marker = "***";
}
