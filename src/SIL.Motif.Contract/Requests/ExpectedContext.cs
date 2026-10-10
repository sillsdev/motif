using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Requests;

/// <summary>Portable Selection evidence facts a write must still match inside its store transaction.</summary>
public sealed record SelectionEvidenceExpectation(
    string Origin,
    string? DeclarationName,
    IReadOnlyList<Guid> DeclarationTextIds,
    IReadOnlyList<string> DeclarationAddedWords,
    string? AssessmentSelectionSha256,
    string? RootAssessmentId,
    IReadOnlyList<string> ReplacementAssessmentIds,
    IReadOnlyList<string> MeasurementAssessmentIds,
    IReadOnlyList<string> WarningIdentities);

/// <summary>The Proposal's own Trial a write was made against, instead of the project's current evidence.</summary>
/// <param name="ProposalId">The Proposal the Trial belongs to.</param>
/// <param name="IntentDigest">The intent digest the Trial measured.</param>
/// <param name="RootAssessmentId">The Trial's own parse-time Assessment, which is never the current Assessment.</param>
public sealed record TrialEvidenceExpectation(string ProposalId, string IntentDigest, string RootAssessmentId);

/// <summary>Evidence identity a write must still match when it reaches the store.</summary>
public sealed record ExpectedContext(BaselineToken Baseline)
{
    /// <summary>The selected Text and added-word source identities, in source order.</summary>
    public IReadOnlyList<Guid> TextIds { get; init; } = [];

    /// <summary>The normalized added-word source identities.</summary>
    public IReadOnlyList<string> AddedWords { get; init; } = [];

    /// <summary>The Trial a write made in Proposal mode was read from, checked beside the source Baseline.</summary>
    public TrialEvidenceExpectation? Trial { get; init; }

    /// <summary>The declaration and Assessment evidence captured by a Selection reader, when supplied.</summary>
    public SelectionEvidenceExpectation? SelectionEvidence { get; init; }
}
