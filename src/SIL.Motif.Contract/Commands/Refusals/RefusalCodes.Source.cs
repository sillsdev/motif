namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>More than one possibility list is named "Motif Sources", so no Source list can be chosen.</summary>
    public const string SourceListAmbiguous = "source.list-ambiguous";
    /// <summary>The project's Source list has no Source with that id.</summary>
    public const string SourceNotFound = "source.not-found";
    /// <summary>A Source read named no id.</summary>
    public const string SourceIdInvalid = "source.id-invalid";
    /// <summary>A chapter reference is not a book and a chapter number, such as MAT 5.</summary>
    public const string SourceRefInvalid = "source.ref-invalid";
    /// <summary>The folder named for a Source does not exist on this machine.</summary>
    public const string SourceFolderMissing = "source.folder-missing";
    /// <summary>The Source's opened Paratext text has no such chapter, so it cannot be made Current.</summary>
    public const string SourceChapterNotInSource = "source.chapter-not-in-source";
    /// <summary>The id of a new Source is already used by a Source in the list or staged in the pending changes.</summary>
    public const string SourceIdInUse = "source.id-in-use";
    /// <summary>The id of a new Source belongs to a removed Source; restore that Source instead.</summary>
    public const string SourceIdRemoved = "source.id-removed";
    /// <summary>The Context text does not parse in the Source's versification.</summary>
    public const string SourceContextInvalid = "source.context-invalid";
    /// <summary>The versification is not one of the built-in names.</summary>
    public const string SourceVersificationUnknown = "source.versification-unknown";
    /// <summary>The Source kind is modelled but not built.</summary>
    public const string SourceKindUnsupported = "source.kind-unsupported";
    /// <summary>The Source's text has no word at this location, so there is no Occurrence to approve or clear.</summary>
    public const string SourceOccurrenceNotFound = "source.occurrence-not-found";
    /// <summary>The word at this location has no letter, so it is not an Occurrence that can be approved.</summary>
    public const string SourceOccurrenceNotEligible = "source.occurrence-not-eligible";
    /// <summary>The analysis chosen belongs to a different wordform than the word at this location.</summary>
    public const string SourceAnalysisMismatch = "source.analysis-mismatch";
    /// <summary>Approving needs a stored analysis, or a parser reading with its wordform, and the request gave neither or both.</summary>
    public const string SourceAnalysisNeeded = "source.analysis-needed";
    /// <summary>The wordform for a parser reading is not in the project, so its analysis cannot be added.</summary>
    public const string SourceWordformMissing = "source.wordform-missing";
    /// <summary>An AI named the reserved pending-changes Draft for a Source edit; it must use a named Proposal.</summary>
    public const string SourceAiNeedsProposal = "source.ai-needs-proposal";
    /// <summary>The Source has no derived word-place index.</summary>
    public const string SourceIndexMissing = "source.index-missing";
    /// <summary>The Source index is stale or has a refresh pending.</summary>
    public const string SourceIndexStale = "source.index-stale";
    /// <summary>The indexed generation changed since the continuation was issued.</summary>
    public const string SourceGenerationChanged = "source.generation-changed";
}
