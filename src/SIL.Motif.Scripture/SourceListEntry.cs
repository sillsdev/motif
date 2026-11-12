namespace SIL.Motif.Scripture;

/// <summary>What kind of material a Source is. Only <see cref="Paratext"/> is built; the other is recorded so the list can already say it.</summary>
public enum SourceKind
{
    /// <summary>A whole Paratext project folder.</summary>
    Paratext,

    /// <summary>A folder of plain text files, whose Context will be glob patterns (not built yet).</summary>
    Corpus,
}

/// <summary>
/// One item of the "Motif Sources" list as read from a project. An item that cannot be used is still returned,
/// with <see cref="Error"/> saying why, so one bad item never hides the others and never throws.
/// </summary>
/// <param name="Id">The Source id: the item's Abbreviation, such as <c>WEB</c>.</param>
/// <param name="Name">The display name: the item's Name.</param>
/// <param name="Kind">The kind of Source; Paratext when the item does not say.</param>
/// <param name="ContextText">The Context string exactly as stored: the item's Description.</param>
/// <param name="VersificationName">The versification name as stored, such as <c>English</c>; empty when the item has none.</param>
/// <param name="Context">The parsed Context; null when <see cref="Error"/> is set.</param>
/// <param name="Error">A readable reason the item is unusable, or null when it is fine.</param>
public sealed record SourceListEntry(
    string Id,
    string Name,
    SourceKind Kind,
    string ContextText,
    string VersificationName,
    ScriptureContext? Context,
    string? Error)
{
    /// <summary>Whether the item can be used as it is.</summary>
    public bool IsValid => Error is null;
}

/// <summary>More than one possibility list is named "Motif Sources", so no list can be chosen. Rename or remove the extras in FieldWorks.</summary>
public sealed class SourceListAmbiguousException(string message) : Exception(message)
{
    /// <summary>The code a command reports with this refusal.</summary>
    public const string Code = "source-list-ambiguous";
}
