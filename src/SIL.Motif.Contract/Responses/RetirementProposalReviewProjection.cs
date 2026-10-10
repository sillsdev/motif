using System.Collections.Generic;
using SIL.Motif.Contract.Retirement;

namespace SIL.Motif.Contract.Responses;

/// <summary>The whole sound-rule retirement review, bound to one Proposal and its observed Dry Run.</summary>
public sealed record RetirementProposalReviewProjection(
    string ProposalId,
    string IntentDigest,
    DryRunProjection DryRun,
    RetirementReviewStatistics Statistics,
    IReadOnlyList<RetirementReviewPart> Parts,
    IReadOnlyList<RetirementAffectedReading> AffectedReadings,
    RetirementFindingReview Finding,
    RetirementUnresolvedReferenceReview UnresolvedReferences,
    IReadOnlyList<string> Unavailable)
{
    /// <summary>Observed effects without a defensible direct operation link remain visible here.</summary>
    public IReadOnlyList<EffectView> UnlinkedEffects { get; init; } = [];

    /// <summary>Proposal operations not assigned to one of the four retirement parts.</summary>
    public IReadOnlyList<string> UnlinkedOperationIds { get; init; } = [];
}

/// <summary>One connected group of operations in the same atomic Proposal.</summary>
public sealed record RetirementReviewPart(
    string Id,
    string Title,
    IReadOnlyList<string> OperationIds,
    IReadOnlyList<EffectView> Effects)
{
    /// <summary>The exact source rows that make this part inspectable.</summary>
    public IReadOnlyList<RetirementReviewDetail> Details { get; init; } = [];
}

/// <summary>One frozen human reading and its exact before/after parser result.</summary>
public sealed record RetirementAffectedReading(
    string CaseId,
    string ReadingId,
    string? WordformId,
    string Word,
    string WritingSystem,
    string SurfaceBefore,
    string? SurfaceAfter,
    string Opinion,
    string OpinionGlyph,
    string VerificationStatus,
    bool? BeforeProduced,
    bool? AfterProduced,
    bool? SurfaceUnchanged,
    string RuleAttributionStatus,
    string? RuleId,
    string? AttributionDigest,
    string? AttributionDetail);

/// <summary>The detector's exact paired counts and resolution state, apart from any judgment disposition.</summary>
public sealed record RetirementFindingReview(
    string FindingId,
    int BeforeNumerator,
    int BeforeDenominator,
    int AfterNumerator,
    int AfterDenominator,
    string BeforeEvidenceDigest,
    string AfterEvidenceDigest,
    bool Resolved,
    bool? Suppressed)
{
    /// <summary>The Draft operations that recorded the finding disposition used to bind this review.</summary>
    public IReadOnlyList<string> BindingOperationIds { get; init; } = [];

    /// <summary>The observed effects of the finding-binding operations.</summary>
    public IReadOnlyList<EffectView> BindingEffects { get; init; } = [];
}

/// <summary>Exact blockers and detail rows when one or more replacement destinations are absent.</summary>
public sealed record RetirementUnresolvedReferenceReview(
    int UnresolvedApprovedAnalyses,
    int UnresolvedAdhocRules,
    int OtherReferences,
    int CustomReferences,
    IReadOnlyList<RetirementReviewDetail> Rows)
{
    /// <summary>The refusal sentence returned by the destination check.</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>One identity-linked row available from the retirement detail manifest.</summary>
public sealed record RetirementReviewDetail(
    string Id,
    string Kind,
    string Label,
    string? WordformId,
    string? AnalysisId,
    string? BundleId,
    string? RuleId,
    string? Opinion,
    string? Field,
    int? Occurrence,
    string? BeforeText,
    string? AfterText);
