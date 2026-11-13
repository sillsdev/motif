namespace SIL.Motif.Model.Snapshot;

/// <summary>
/// The fields the Motif Sources operations read and write. Hand-written like the judgment field: the custom fields
/// are created in the project at Apply, so the generator's model file does not know them.
/// </summary>
public static partial class SnapshotFields
{
    /// <summary>The Sources of the project: the items of the "Motif Sources" list, found through the language project.</summary>
    public const string ScriptureSourceItems = "scripture/langProject/sources";

    /// <summary>A <c>CmPossibility</c>'s <c>Description</c> MultiString field, which holds a Source's Context.</summary>
    public const string CmPossibilityDescription = "lists/cmPossibility/description";

    /// <summary>A <c>CmPossibility</c>'s reserved custom String field that holds a Source's kind.</summary>
    public const string CmPossibilityMotifSourceKind = "lists/cmPossibility/motifSourceKind";

    /// <summary>A <c>CmPossibility</c>'s reserved custom String field that holds a Source's versification name.</summary>
    public const string CmPossibilityMotifSourceVersification = "lists/cmPossibility/motifSourceVersification";

    /// <summary>A <c>CmPossibility</c>'s reserved custom String field that holds a Source's status.</summary>
    public const string CmPossibilityMotifSourceStatus = "lists/cmPossibility/motifSourceStatus";
}
