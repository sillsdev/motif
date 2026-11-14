using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>One operation an authoring or composing verb added, as it is echoed back to the caller.</summary>
public sealed record OperationSummary(string OperationId, string Kind, string? EntityId = null);

/// <summary>A freshly created Draft, not yet finalized.</summary>
public sealed record DraftCreatedResponse(string DraftName, string ProposalId, string? Label);

/// <summary>A <c>lexical/lexSense/setGloss</c> value authored in a Draft by <c>add-set-gloss</c>.</summary>
public sealed record SetGlossAddedResponse(
    string DraftName,
    string OperationId,
    string Target,
    string Ws,
    string Text,
    IReadOnlyList<string> DependsOn,
    int OperationCount,
    bool ReplacedPriorValue = false);

/// <summary>A <c>lexical/lexEntry/deleteLexemeForm</c> operation appended by <c>add-delete-lexeme-form</c>.</summary>
public sealed record DeleteLexemeFormAddedResponse(
    string DraftName, string OperationId, string Target, int OperationCount);

/// <summary>One existing grammar object affected by a composed slot change.</summary>
public sealed record ComposedRelatedObject(string Kind, string Id, string Name);

/// <summary>One language-project object a composer actually creates.</summary>
/// <param name="Id">The object's canonical identity, taken from its creating operation.</param>
/// <param name="Kind">The semantic kind of the operation that creates the object.</param>
public sealed record CreatedObject(string Id, string Kind);

/// <summary>
/// The operations one composer resolved and appended to a Draft. <c>Outcome</c> is written only when
/// nothing was staged because the request was already recorded (<c>already-recorded</c>).
/// </summary>
public sealed record ComposedOperationsResponse(
    string DraftName, string ComposerName, IReadOnlyList<OperationSummary> Operations, int OperationCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ComposedRelatedObject>? RelatedObjects = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Outcome = null)
{
    /// <summary>The canonical identities of objects the returned operations create.</summary>
    public IReadOnlyList<CreatedObject> CreatedObjects { get; init; } = [];
}

/// <summary>The operations one intent of a batch resolved to, by its position in the batch.</summary>
public sealed record ComposedIntentResult(
    int Index, string Verb, string ComposerName, IReadOnlyList<OperationSummary> Operations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ComposedRelatedObject>? RelatedObjects = null)
{
    /// <summary>The canonical identities this batch intent's operations create.</summary>
    public IReadOnlyList<CreatedObject> CreatedObjects { get; init; } = [];
}

/// <summary>
/// Every intent of one <c>compose-batch</c> call, all appended to the Draft together; <c>OperationCount</c> is the
/// Draft's operation count afterwards.
/// </summary>
public sealed record ComposedBatchResponse(
    string DraftName, IReadOnlyList<ComposedIntentResult> Intents, int OperationCount);

/// <summary>
/// A <c>lexical/lexSense/setGloss</c> operation appended by <c>promote-gloss</c>, evidenced by a
/// stored corpus.
/// </summary>
public sealed record PromoteGlossAddedResponse(
    string DraftName,
    string OperationId,
    string Target,
    string Ws,
    string Text,
    string CorpusId,
    string? Licence,
    int OperationCount);

/// <summary>One kept version of a Proposal's Rationale, which a person may restore while the Proposal is open.</summary>
/// <param name="RevisionId">The version's id, larger for a later one.</param>
/// <param name="Markdown">The Rationale as it was saved.</param>
/// <param name="AuthorLabel">What the agent that wrote it called itself; null when a person wrote it.</param>
/// <param name="PersonName">The person it was by or for; empty when unknown.</param>
/// <param name="CreatedUtc">When it was saved, as a round-trip UTC timestamp.</param>
public sealed record RationaleRevisionView(long RevisionId, string Markdown, string? AuthorLabel, string PersonName, string CreatedUtc);

/// <summary>A Proposal's Rationale or the person's note written or cleared.</summary>
/// <param name="ProposalId">The Proposal the text belongs to.</param>
/// <param name="Field">Which text changed: <c>rationale</c> or <c>note</c>.</param>
/// <param name="UpdatedUtc">When the text was written; null when a note was cleared.</param>
/// <param name="CommentsChanged">Whether a person's Comment is now newer than the Rationale.</param>
public sealed record ProposalTextChangedResponse(string ProposalId, string Field, string? UpdatedUtc, bool CommentsChanged)
{
    /// <summary>
    /// What a reader would see as wrong in the Rationale as saved (ADR 0099); empty when it is clean. A Draft's
    /// Rationale saves with findings, but the Draft cannot be finalized until they are fixed.
    /// </summary>
    public IReadOnlyList<TextFinding> Findings { get; init; } = [];

    /// <summary>
    /// The Rationale as stored: the text written, tidied when it is clean (ADR 0099). The base for its next edit;
    /// null for a note.
    /// </summary>
    public string? Text { get; init; }
}

/// <summary>A Comment was added, edited or deleted on a Proposal.</summary>
/// <param name="ProposalId">The Proposal the Comment is on.</param>
/// <param name="CommentId">The Comment's number.</param>
/// <param name="Change"><c>added</c>, <c>edited</c> or <c>deleted</c>.</param>
/// <param name="CommentsChanged">Whether a person's Comment is now newer than the builder's last write.</param>
public sealed record ProposalCommentChangedResponse(string ProposalId, long CommentId, string Change, bool CommentsChanged)
{
    /// <summary>What a reader would see as wrong in the Comment as saved; Comments save with findings and never block.</summary>
    public IReadOnlyList<TextFinding> Findings { get; init; } = [];
}

/// <summary>
/// Something a reader would see as wrong in a Rationale or Comment read as FieldWorks Markdown: unreadable markup, a
/// link to an object not in the project, an unknown writing system or style (ADR 0099 decision 4).
/// </summary>
/// <param name="Id">The checker's stable finding id, such as <c>fwmd.link.not-found</c>.</param>
/// <param name="Line">The 1-based line of the text the finding starts on.</param>
/// <param name="Column">The 1-based column, in UTF-16 code units.</param>
/// <param name="Message">What is wrong, in a reader's words.</param>
public sealed record TextFinding(string Id, int Line, int Column, string Message)
{
    /// <summary>The text the finding is about, when the checker names it.</summary>
    public string? Observed { get; init; }

    /// <summary>One exact replacement of <see cref="Observed"/> that fixes the finding, or null when there is none.</summary>
    public string? Replacement { get; init; }
}

/// <summary>The Draft field <c>label</c> set to a new value.</summary>
public sealed record DraftFieldChangedResponse(string DraftName, string Field, string Value);

/// <summary>A Draft committed as a Proposal revision, either its first (Finalized) or a later one (Amended).</summary>
public sealed record ProposalFinalizedResponse(
    string DraftName, string ProposalId, string IntentDigest, int OperationCount, bool IsAmend);

/// <summary>A Draft removed from the store, never committed.</summary>
public sealed record DraftDiscardedResponse(string DraftName, bool WasReopened);

/// <summary>A finalized Proposal's content reopened as an editable Draft under its own id.</summary>
public sealed record ReopenedResponse(
    string ProposalId, string DraftName, string? CurrentIntentDigest, int OperationCount);

/// <summary>A finalized Proposal's content copied into a brand-new Draft under a freshly minted id.</summary>
public sealed record DuplicatedResponse(
    string SourceProposalId, string DraftName, string ProposalId, int OperationCount);

/// <summary>One operation removed only because it depended on an operation the caller asked to remove.</summary>
public sealed record RemovedDependent(string OperationId, string Kind);

/// <summary>The operations removed from a Draft, and any dependent operations <c>--force</c> also removed.</summary>
public sealed record OperationsRemovedResponse(
    string DraftName,
    IReadOnlyList<string> RemovedOperationIds,
    IReadOnlyList<RemovedDependent> ForcedDependents,
    int OperationCount);

/// <summary>One new Draft <c>split</c> produced from a source Proposal's operations.</summary>
public sealed record SplitDraftResult(string DraftName, string ProposalId, int OperationCount);

/// <summary>A committed Proposal's operations partitioned into brand-new Drafts.</summary>
public sealed record ProposalSplitResponse(
    string SourceProposalId, IReadOnlyList<SplitDraftResult> Drafts, int ForcedSeveredEdgeCount);

/// <summary>A Proposal moved to a new status (<c>defer</c>, <c>reject</c>, or <c>supersede</c>).</summary>
public sealed record ProposalStatusChangedResponse(string ProposalId, string Status, string? RelatedProposalId);

/// <summary>One picture attached to a Proposal, without its bytes.</summary>
/// <param name="PictureId">The picture's number, which a remove or a read names.</param>
/// <param name="CommentId">The Comment the picture is attached to; null when it is attached to the Proposal itself.</param>
/// <param name="MediaType"><c>image/webp</c>, <c>image/png</c> or <c>image/jpeg</c>.</param>
/// <param name="Width">The picture's width in pixels.</param>
/// <param name="Height">The picture's height in pixels.</param>
/// <param name="ByteCount">How many bytes the stored picture takes, counted in the store's size budget.</param>
/// <param name="CreatedUtc">When it was added.</param>
/// <param name="Caption">The one-line caption the person gave it; null when it has none.</param>
public sealed record PictureView(
    long PictureId, long? CommentId, string MediaType, int Width, int Height, int ByteCount, string CreatedUtc,
    string? Caption = null);

/// <summary>A picture added to or removed from a Proposal.</summary>
/// <param name="ProposalId">The Proposal's id.</param>
/// <param name="PictureId">The picture's number.</param>
/// <param name="Change"><c>added</c> or <c>removed</c>.</param>
/// <param name="Picture">The picture just added; null after a removal.</param>
public sealed record ProposalPictureChangedResponse(string ProposalId, long PictureId, string Change, PictureView? Picture);

/// <summary>A Proposal's pictures, and the bytes of one of them when it was asked for.</summary>
/// <param name="ProposalId">The Proposal's id.</param>
/// <param name="Pictures">Every picture on the Proposal, oldest first.</param>
/// <param name="DataBase64">The bytes of the asked-for picture, Base64; null when none was asked for.</param>
public sealed record ProposalPicturesResponse(
    string ProposalId, IReadOnlyList<PictureView> Pictures, string? DataBase64 = null);

/// <summary>An empty Draft that stayed because it could not be discarded, with the reason.</summary>
public sealed record EmptyDraftKept(string DraftName, string Code, string Reason);

/// <summary>The never-finalized empty Drafts removed from the store, and any that stayed.</summary>
public sealed record EmptyDraftsDiscardedResponse(IReadOnlyList<string> Discarded, IReadOnlyList<EmptyDraftKept> Kept);

/// <summary>A Source change staged in the pending changes.</summary>
/// <param name="Source">The Source id as the change names it.</param>
/// <param name="DraftName">The pending-changes Draft that now holds the change.</param>
/// <param name="Operations">The operations staged.</param>
/// <param name="Folder">The folder recorded on this machine by the same command; null when none was given.</param>
public sealed record SourceStageResponse(
    string Source, string DraftName, IReadOnlyList<OperationSummary> Operations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Folder = null);
