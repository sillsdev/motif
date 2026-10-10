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

/// <summary>Evidence identity a write must still match when it reaches the store.</summary>
public sealed record ExpectedContext(BaselineToken Baseline)
{
    /// <summary>The selected Text and added-word source identities, in source order.</summary>
    public IReadOnlyList<Guid> TextIds { get; init; } = [];

    /// <summary>The normalized added-word source identities.</summary>
    public IReadOnlyList<string> AddedWords { get; init; } = [];

    /// <summary>The declaration and Assessment evidence captured by a Selection reader, when supplied.</summary>
    public SelectionEvidenceExpectation? SelectionEvidence { get; init; }
}
