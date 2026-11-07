using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Review;

/// <summary>A frozen assertion made independently of the candidate being measured.</summary>
public sealed record ReviewAssertion(
    string Id, string ReadingIdentity, ApprovedMorphology? Morphology, ReferenceAuthority Authority,
    string ActorId, string RevisionId, bool Approved, bool WholeWordNegative, bool Eligible,
    IReadOnlyList<string> SourceRefs)
{
    /// <summary>The recorded context of a surface-negative assertion, when present.</summary>
    public string? Context { get; init; }
}

/// <summary>A frozen membership of a word in a project-named review set.</summary>
public sealed record ReviewMembership(
    string SetId, string CaseId, string Role, long Weight, string? CellId, string? VariantId,
    IReadOnlyList<string> ExpectationIds)
{
    public string? OccurrenceId { get; init; }
    public string? TextId { get; init; }
    public string? SourceLocator { get; init; }
    public long? Position { get; init; }
    public string? LexemeId { get; init; }
    public IReadOnlyList<FeatureValue> Features { get; init; } = [];
    public IReadOnlyList<string> SourceRefs { get; init; } = [];
}

/// <summary>One exact word case and the evidence captured before the report is produced.</summary>
public sealed record ReviewCaseFacts(
    string CaseKey, string WordKey, string Word, string? WritingSystemId,
    ParseWordEvidence? Before, ParseWordEvidence? After,
    IReadOnlyList<ReviewAssertion> Assertions, IReadOnlyList<ReviewMembership> Memberships)
{
    public bool IdentityAvailable { get; init; }
}

/// <summary>One observed loss of a frozen reading identity.</summary>
public sealed record ReviewReadingLoss(
    string ReadingIdentity, bool Approved, bool ReferenceConfirmed, IReadOnlyList<string> AssertionIds);

/// <summary>Classified observations and retained references for one exact case.</summary>
public sealed record ReviewCaseDelta(
    string CaseKey, string WordKey, string Word, string? WritingSystemId,
    bool NewNoParse, bool NewApprovedLoss, bool NewlyAcceptedNegative,
    IReadOnlyList<ReviewReadingLoss> ReadingLosses, IReadOnlyList<ReviewFlagId> Flags,
    IReadOnlyList<ReviewMembership> Memberships, IReadOnlyList<string> Limitations)
{
    public ReviewCaseSideFacts Before { get; init; } = ReviewCaseSideFacts.NotMeasured;
    public ReviewCaseSideFacts After { get; init; } = ReviewCaseSideFacts.NotMeasured;
    public IReadOnlyList<ReviewAssertion> Assertions { get; init; } = [];

    /// <summary>Whether the complete frozen assertion list was captured.</summary>
    public bool AssertionsCaptured { get; init; }

    /// <summary>Frozen observed readings; null means their complete list was not captured.</summary>
    public IReadOnlyList<ReadingRow>? Readings { get; init; }

    /// <summary>Frozen source projections; null means their complete list was not captured.</summary>
    public IReadOnlyList<SourceRow>? Sources { get; init; }

    /// <summary>Whether exact complete reading identities changed, or null when that cannot be established.</summary>
    public bool? Changed { get; init; }
}

/// <summary>Observed presence and the expectations that one side could establish.</summary>
public sealed record ReviewCaseSideFacts(
    Completion Completion, bool? Parsed, IReadOnlyList<string> StopReasons,
    IReadOnlyList<string> MatchedExpectationIds, IReadOnlyList<string> MissingExpectationIds,
    IReadOnlyList<string> UnknownExpectationIds)
{
    /// <summary>A case for which the search was not measured.</summary>
    public static ReviewCaseSideFacts NotMeasured { get; } = new(global::SIL.Motif.Contract.Review.Completion.NotMeasured, null, [], [], [], []);
}

/// <summary>One checkpoint's exact parser and scope identity for a work comparison.</summary>
public sealed record ReviewWorkScope(
    string ScopeDigest, string BaselineTokenDigest, string Assessor, string? ParserDigest,
    string? CounterSemantics, string SettingsDigest, string TokeniserDigest);

/// <summary>One case's internal work evidence; counters are never a report output field.</summary>
public sealed record ReviewWorkCase(string CaseKey, bool Complete, long? Work);

/// <summary>Both scope pins and counters selected for a same-scope comparison.</summary>
public sealed record ReviewWorkInputs(
    ReviewWorkScope BeforeScope, ReviewWorkScope AfterScope, IReadOnlyList<string> RequiredCases,
    IReadOnlyList<ReviewWorkCase> Before, IReadOnlyList<ReviewWorkCase> After);

/// <summary>One stable diagnostic identity and its warning-level state.</summary>
public sealed record ReviewDiagnostic(
    string? ProducerId, string Origin, string Code, IReadOnlyList<string>? SubjectIds,
    IReadOnlyList<string> Qualifiers, string Severity);

/// <summary>One frozen Parsimony finding and the evidence digest that accompanies it.</summary>
public sealed record ReviewFinding(string FindingId, string EvidenceDigest, string? Disposition);

/// <summary>One measured Parsimony universe at a checkpoint.</summary>
public sealed record ReviewFindingMeasure(
    string Group, string MeasureId, string Version, string ThresholdVersion, string UniverseDigest,
    string Unit, bool Completed, IReadOnlyList<ReviewFinding> Findings);

/// <summary>Paired immutable diagnostic and Parsimony evidence.</summary>
public sealed record ReviewFindingInputs(
    IReadOnlyList<ReviewDiagnostic>? BeforeWarnings, IReadOnlyList<ReviewDiagnostic>? AfterWarnings,
    IReadOnlyList<ReviewFindingMeasure>? BeforeParsimony, IReadOnlyList<ReviewFindingMeasure>? AfterParsimony);

/// <summary>A Parsimony measure's added, removed and evidence-changed finding identities.</summary>
public sealed record ReviewMeasureDelta(
    string Group, string MeasureId, string Unit, IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed, IReadOnlyList<string> EvidenceChanged, string? Limitation);

/// <summary>Independent warning and Parsimony events, with any missing-comparison reasons.</summary>
public sealed record ReviewFindingDelta(
    IReadOnlyList<string> AddedWarnings, IReadOnlyList<string> RemovedWarnings,
    IReadOnlyList<ReviewMeasureDelta> Measures, IReadOnlyList<string> Limitations);

/// <summary>A configured set with the exact membership and expectations bound for a Trial.</summary>
public sealed record ReviewSetBinding(
    string Id, string Name, string Role, string MembershipDigest, string ExpectationDigest,
    string SourceDigest, IReadOnlyList<string> SourceRefs);

/// <summary>A planned case and its memberships, captured even when a search never starts.</summary>
public sealed record ReviewPlannedCase(
    string CaseKey, string WordKey, string Word, string? WritingSystemId, string? WordformId,
    IReadOnlyList<ReviewMembership> Memberships, IReadOnlyList<ReviewAssertion> Assertions);

/// <summary>The content-bound inputs used on both sides of one Trial.</summary>
public sealed record FrozenReviewInputs(
    string ProjectKey, string ProposalId, string ScopeDigest, string EvidenceManifestDigest,
    string? ParserDigest, IReadOnlyList<ReviewSetBinding> Sets, IReadOnlyList<string> OmittedSetIds,
    IReadOnlyList<ReviewPlannedCase> Cases);
