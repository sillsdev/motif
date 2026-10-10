using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Parsimony;

/// <summary>The frozen positive and negative input shape consumed by Parsimony measures and paired verification.</summary>
/// <param name="Contract">The versioned shape identifier.</param>
/// <param name="ProjectId">The FieldWorks project whose saved input was projected.</param>
/// <param name="Digest">A digest of the complete normalized expectation input.</param>
/// <param name="ReviewedNegatives">Human-confirmed surface and reading cases from the Notebook.</param>
/// <param name="ApprovedReadings">Native default-human Approved readings, projected separately.</param>
/// <param name="DisapprovedReadings">Native default-human Disapproved readings, projected separately.</param>
/// <param name="Issues">Conflicting and unavailable input that must not enter unqualified rates.</param>
public sealed record ParsimonyExpectationProjection(
    string Contract,
    string ProjectId,
    string Digest,
    IReadOnlyList<ReviewedNegativeExpectation> ReviewedNegatives,
    IReadOnlyList<NativeReadingExpectation> ApprovedReadings,
    IReadOnlyList<NativeReadingExpectation> DisapprovedReadings,
    IReadOnlyList<ParsimonyExpectationIssue> Issues)
{
    /// <summary>The exact input contract for restrictiveness measures and paired verification.</summary>
    public const string ContractVersion = "parsimony-expectations/v1";
}

/// <summary>One effective Notebook negative, retaining exact case and revision provenance.</summary>
public sealed record ReviewedNegativeExpectation(
    string CaseId,
    string JudgmentId,
    string RevisionId,
    string NotebookRecordId,
    string ContentDigest,
    string WritingSystem,
    string Form,
    string Context,
    NegativeJudgmentTarget Target,
    string? WordformId,
    string? AnalysisId,
    string? Reason,
    JudgmentActor Actor,
    DateTimeOffset? JudgedAtUtc,
    JudgmentSource? Source,
    string Status,
    string? Issue);

/// <summary>One deduplicated native Opinion signature for a surface and writing system.</summary>
/// <param name="Opinion">The authoritative default-human Opinion stored by FieldWorks.</param>
/// <param name="WritingSystem">The exact writing-system tag for the surface form.</param>
/// <param name="Form">The surface form normalized to NFD.</param>
/// <param name="Morphs">The ordered Form/MSA/InflType identity, with nulls preserved as unavailable data.</param>
/// <param name="SourceWordformIds">Every source wordform identity represented by this signature.</param>
/// <param name="SourceAnalysisIds">Every source analysis identity represented by this signature.</param>
/// <param name="UnavailableReason">Why the morphology cannot support an exact parser comparison, if applicable.</param>
public sealed record NativeReadingExpectation(
    string Opinion,
    string WritingSystem,
    string Form,
    IReadOnlyList<ParseMorph> Morphs,
    IReadOnlyList<string> SourceWordformIds,
    IReadOnlyList<string> SourceAnalysisIds,
    string? UnavailableReason);

/// <summary>A visible conflict or unavailable source that remains outside expectation denominators.</summary>
public sealed record ParsimonyExpectationIssue(string SourceKind, string SourceId, string Code, string Detail);
