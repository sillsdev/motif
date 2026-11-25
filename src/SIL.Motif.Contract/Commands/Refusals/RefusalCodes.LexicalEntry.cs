namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A lexical-entry read named no entry or sense id.</summary>
    public const string LexicalEntryNoId = "lexical-entry.no-id";
    /// <summary>A lexical-entry read named an id that is not a portable id.</summary>
    public const string LexicalEntryInvalidId = "lexical-entry.invalid-id";
    /// <summary>A lexical-entry read named more ids than one call reads.</summary>
    public const string LexicalEntryTooManyIds = "lexical-entry.too-many-ids";
    /// <summary>The current Baseline file is missing, so lexical entries cannot be read.</summary>
    public const string LexicalEntryBaselineUnavailable = "lexical-entry.baseline-unavailable";
}
