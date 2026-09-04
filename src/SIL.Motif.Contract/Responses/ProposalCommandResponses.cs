using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>One operation an authoring or composing verb added, as it is echoed back to the caller.</summary>
public sealed record OperationSummary(string OperationId, string Kind);

/// <summary>A freshly created Draft, not yet finalized.</summary>
public sealed record DraftCreatedResponse(string DraftName, string ProposalId, string? Label);

/// <summary>A <c>lexical/lexSense/setGloss</c> operation appended to a Draft by <c>add-set-gloss</c>.</summary>
public sealed record SetGlossAddedResponse(
    string DraftName,
    string OperationId,
    string Target,
    string Ws,
    string Text,
    IReadOnlyList<string> DependsOn,
    int OperationCount);

/// <summary>A <c>lexical/lexEntry/deleteLexemeForm</c> operation appended by <c>add-delete-lexeme-form</c>.</summary>
public sealed record DeleteLexemeFormAddedResponse(
    string DraftName, string OperationId, string Target, int OperationCount);

/// <summary>The operations one composer resolved and appended to a Draft.</summary>
public sealed record ComposedOperationsResponse(
    string DraftName, string ComposerName, IReadOnlyList<OperationSummary> Operations, int OperationCount);

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

/// <summary>The Draft field <c>label</c> or <c>comment</c> set to a new value.</summary>
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
