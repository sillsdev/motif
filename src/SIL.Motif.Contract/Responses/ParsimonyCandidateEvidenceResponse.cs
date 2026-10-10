using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Contract.Responses;

/// <summary>The exact Proposal and source bindings used to build candidate Parsimony evidence.</summary>
public sealed record ParsimonyCandidateBinding(
    string IdentitySha256,
    BaselineToken BaselineToken,
    string DryRunJobId,
    string ProposalId,
    string ProposalIntentDigest,
    string ProposalContentSha256,
    IReadOnlyList<ParsimonyPrerequisiteBinding> Prerequisites,
    string DryRunEffectDigest,
    string DryRunAnchorDigest,
    string BaselineSourceSha256,
    string CandidateSourceSha256,
    string HumanJudgmentDigest);

/// <summary>One prerequisite revision in the order it was applied to the candidate copy.</summary>
public sealed record ParsimonyPrerequisiteBinding(string ProposalId, string IntentDigest, string ContentSha256);

/// <summary>One stable recommendation identity and its before/after evidence.</summary>
public sealed record ParsimonyCandidateFindingChange(string FindingId, string Change,
    ParsimonyFinding? Before, ParsimonyFinding? After);

/// <summary>An Approved analysis tuple kept from the Baseline while assessing candidate preservation.</summary>
public sealed record ParsimonyApprovedAnalysisTuple(string WordformGuid, string AnalysisGuid,
    string ContentSha256);

/// <summary>Whether exact Approved analysis identities remain available in the candidate evidence.</summary>
public sealed record ParsimonyApprovedAnalysisPreservation(string Status, int BaselineCount,
    int PreservedCount, IReadOnlyList<ParsimonyApprovedAnalysisTuple> UnmatchedBaselineTuples);

/// <summary>The paired Parsimony Reports and exact Proposal binding for one Dry Run candidate.</summary>
public sealed record ParsimonyCandidateEvidenceResponse(
    int SchemaVersion,
    string MeasureId,
    ParsimonyCandidateBinding Candidate,
    ParsimonyReportResponse Before,
    ParsimonyReportResponse After,
    IReadOnlyList<ParsimonyCandidateFindingChange> FindingChanges,
    ParsimonyApprovedAnalysisPreservation ApprovedAnalysisPreservation);
