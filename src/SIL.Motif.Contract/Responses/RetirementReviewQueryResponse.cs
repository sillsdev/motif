namespace SIL.Motif.Contract.Responses;

/// <summary>The review state Motif can read for one staged allomorph-retirement Draft.</summary>
public sealed record RetirementReviewQueryResponse(
    bool Applicable,
    string DraftId,
    string State,
    string? DryRunJobId,
    DryRunProjection? DryRun,
    RetirementProposalReviewProjection? Review,
    IReadOnlyList<string> Unavailable);
