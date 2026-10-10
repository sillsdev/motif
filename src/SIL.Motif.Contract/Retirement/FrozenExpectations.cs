using System.Collections.Generic;
using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Retirement;

/// <summary>
/// Immutable before input required by retirement verification. Freeze independently of the mutated
/// scratch; include every populated vernacular case and all Approved readings of affected wordforms,
/// even outside the Selection. A manifest digest binds the entire set, never a representative sample.
/// </summary>
public sealed record FrozenExpectationSet(
    BaselineToken Baseline,
    string HumanInputDigest,
    string ManifestDigest,
    string ExpectationRevision,
    IReadOnlyList<FrozenExpectationCase> Cases,
    IReadOnlyList<FrozenReviewedNegative> ReviewedNegatives,
    IReadOnlyList<string> Unavailable)
{
    public string Contract { get; init; } = AllomorphRetirementCapabilities.FrozenExpectationContract;
}

/// <summary>
/// One separate surface/WS case, with original wordform identity where present, explicit Selection
/// membership and held-out provenance. Every original Approved morphology remains a distinct expectation.
/// </summary>
public sealed record FrozenExpectationCase(
    string CaseId, string? Wordform, string WritingSystem, string Surface, bool InSelection, bool HeldOut,
    IReadOnlyList<FrozenExpectedReading> Readings);

/// <summary>
/// The original analysis and human evaluation membership. Opinion is approved, disapproved, unknown
/// or mixed; parser evaluation is not human approval. ProvenanceDigest binds other retained analysis data.
/// </summary>
public sealed record FrozenExpectedReading(
    string ReadingId, string? Analysis, string Opinion, string ProvenanceDigest,
    IReadOnlyList<FrozenHumanEvaluation> Evaluations, IReadOnlyList<FrozenExpectationMorph> Morphs);

/// <summary>Exact human EvaluationsRC membership and evaluator identity, independently of parser results.</summary>
public sealed record FrozenHumanEvaluation(string Evaluation, string Agent, string Opinion);

/// <summary>
/// Ordered authoritative morphology for unchanged ADR 0027 comparison, with captured bundle text kept
/// separately from identity. Guessed text/WS stays conditional; ordinary retirement requires neither.
/// </summary>
public sealed record FrozenExpectationMorph(
    string? Bundle, string? Form, string Msa, string? InflType, string ExpansionRole,
    string? GuessedString, string? GuessedWritingSystem, IReadOnlyList<FrozenBundleText> BundleText);

/// <summary>Original MultiString content and formatting digest; native copied/cleared effects are separate evidence.</summary>
public sealed record FrozenBundleText(string WritingSystem, string Text, string RichContentDigest);

/// <summary>
/// A project-backed forbidden surface or ordered reading with exact judgment revision provenance.
/// Empty Morphs is allowed only for surface targets. Translating a reading needs an authored new revision.
/// </summary>
public sealed record FrozenReviewedNegative(
    string CaseId, string Revision, string ContentDigest, string WritingSystem, string Surface, string Context,
    string Target, IReadOnlyList<FrozenExpectationMorph> Morphs);

/// <summary>
/// Complete distinct review counts derived from enumerated scratch records, not author-supplied estimates.
/// Partitions are disjoint per counted entity; a wordform with different reading Opinions belongs to mixed.
/// </summary>
public sealed record RetirementReviewStatistics(
    RetirementOpinionCounts BundlesRepointed,
    RetirementOpinionCounts AnalysesRepointed,
    RetirementOpinionCounts WordformsRepointed,
    int AssessedFormCases,
    IReadOnlyList<RetirementAdhocCounts> Adhoc,
    int OtherReferences,
    int OwnedObjectsDeleted,
    int BundleTextAlternativesChanged,
    int BundleTextAlternativesCleared,
    RetirementDetectorCounts Detector,
    string DetailManifestDigest);

/// <summary>Distinct ids, including an explicit mixed/contradictory category; Total is the partition sum.</summary>
public sealed record RetirementOpinionCounts(int Approved, int Disapproved, int Unknown, int Mixed)
{
    public long Total() => (long)Approved + Disapproved + Unknown + Mixed;
}

/// <summary>Distinct rule ids and target occurrence counts, partitioned by grouping and enabled state.</summary>
public sealed record RetirementAdhocCounts(bool Grouped, bool Enabled, int Rules, int TargetOccurrences);

/// <summary>Exact before/after detector n/N and item-relevant evidence; resolution is separate from parse preservation.</summary>
public sealed record RetirementDetectorCounts(
    string FindingId, int BeforeNumerator, int BeforeDenominator, int AfterNumerator, int AfterDenominator,
    string BeforeEvidenceDigest, string AfterEvidenceDigest, bool Resolved);
