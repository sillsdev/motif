namespace SIL.Motif.Contract.Requests;

/// <summary>Applies the pending changes that were checked and measured.</summary>
/// <param name="ProjectPath">The FieldWorks project whose pending changes will be applied.</param>
/// <param name="DraftId">The expected Draft identity, or <see langword="null"/> to use the current Draft.</param>
/// <param name="Revision">The expected revision, or <see langword="null"/> to use the current revision.</param>
public sealed record ApplyPendingRequest(
    string ProjectPath, string? DraftId, string? Revision);

/// <summary>Applies one Proposal from its current Trial: a finalized Proposal, or a Draft finalized first.</summary>
/// <param name="ProjectPath">The FieldWorks project the Proposal will be applied to.</param>
/// <param name="ProposalId">The Proposal to apply.</param>
/// <param name="ExpectedRevision">
/// The Draft revision, or the intent digest of a finalized Proposal, the caller reviewed; <see langword="null"/> accepts the current one.
/// </param>
public sealed record ApplyProposalRequest(string ProjectPath, string ProposalId, string? ExpectedRevision);

/// <summary>Measures the selected words against one exact pending revision.</summary>
/// <param name="ProjectPath">The FieldWorks project whose pending changes will be measured.</param>
/// <param name="DraftId">The expected Draft identity, or <see langword="null"/> to use the current Draft.</param>
/// <param name="Revision">The expected revision, or <see langword="null"/> to use the current revision.</param>
/// <param name="Words">The words to include in the Trial.</param>
/// <param name="BeforeCorrectnessAssessmentId">The earlier Correctness Assessment used for comparison, if any.</param>
public sealed record MeasurePendingRequest(
    string ProjectPath,
    string? DraftId,
    string? Revision,
    IReadOnlyList<string> Words,
    string? BeforeCorrectnessAssessmentId = null);
