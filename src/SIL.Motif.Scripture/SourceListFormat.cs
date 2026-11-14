namespace SIL.Motif.Scripture;

/// <summary>
/// How a Source is written in the project's own data: the one place that says which field of the list item holds what.
/// The Motif operations that write Sources must use exactly this mapping.
/// </summary>
/// <remarks>
/// The list follows the Grammar Design Records list of ADR 0083: an unowned possibility list under
/// <c>LangProject.CheckListsOC</c>, found by its Name text, with one item per Source and no sub-items.
/// <list type="bullet">
/// <item><description>List Name: <see cref="ListName"/>, in any writing system. People may edit the list in FieldWorks.</description></item>
/// <item><description>Item Abbreviation: the Source id (for example <c>WEB</c>). It must be present and unique, compared ignoring case.</description></item>
/// <item><description>Item Name: the display name.</description></item>
/// <item><description>Item Description: the Context string, in Machine's range grammar (for example <c>GEN-ROM; 1CO 13-15</c>), as plain text with no formatting.</description></item>
/// <item><description>Custom string field <see cref="KindField"/> on CmPossibility: <c>Paratext</c> or <c>Corpus</c>. Absent or empty means Paratext.</description></item>
/// <item><description>Custom string field <see cref="VersificationField"/> on CmPossibility: one of the built-in versification names in <see cref="Versifications"/>.</description></item>
/// <item><description>Custom string field <see cref="StatusField"/> on CmPossibility: <c>Active</c> or <c>Removed</c>. Absent or empty means Active.
/// A Source is never deleted: removing one sets Removed, and the item keeps its id and Context. A Removed id is never reused.</description></item>
/// </list>
/// The folder of a Source is not stored here: it differs per machine and lives in <see cref="ScriptureLocalStore"/>.
/// The custom fields are created like the human-judgement fields: <c>IFwMetaDataCacheManaged.AddCustomField</c> with
/// class CmPossibility, type String, writing-system selector analysis, no destination class.
/// </remarks>
public static class SourceListFormat
{
    /// <summary>The name of the possibility list that holds the Sources.</summary>
    public const string ListName = SIL.Motif.Runner.Operations.ScriptureSourceFields.ListName;

    /// <summary>The internal name of the custom field that holds the kind.</summary>
    public const string KindField = SIL.Motif.Runner.Operations.ScriptureSourceFields.KindField;

    /// <summary>The internal name of the custom field that holds the versification name.</summary>
    public const string VersificationField = SIL.Motif.Runner.Operations.ScriptureSourceFields.VersificationField;

    /// <summary>The internal name of the custom field that holds the status (<c>Active</c> or <c>Removed</c>).</summary>
    public const string StatusField = SIL.Motif.Runner.Operations.ScriptureSourceFields.StatusField;

    /// <summary>The CmPossibility class name the custom fields belong to.</summary>
    public const string FieldOwnerClass = SIL.Motif.Runner.Operations.ScriptureSourceFields.FieldOwnerClass;

    /// <summary>The kind written as stored text.</summary>
    public static string KindText(SourceKind kind) => kind.ToString();

    /// <summary>The status written as stored text.</summary>
    public static string StatusText(SourceStatus status) => status.ToString();

    /// <summary>The built-in versifications by the name stored in the list, matched ignoring case.</summary>
    public static IReadOnlyDictionary<string, SIL.Scripture.ScrVers> Versifications { get; } =
        new Dictionary<string, SIL.Scripture.ScrVers>(StringComparer.OrdinalIgnoreCase)
        {
            ["Original"] = SIL.Scripture.ScrVers.Original,
            ["English"] = SIL.Scripture.ScrVers.English,
            ["Septuagint"] = SIL.Scripture.ScrVers.Septuagint,
            ["Vulgate"] = SIL.Scripture.ScrVers.Vulgate,
            ["Russian Protestant"] = SIL.Scripture.ScrVers.RussianProtestant,
            ["Russian Orthodox"] = SIL.Scripture.ScrVers.RussianOrthodox,
        };
}
