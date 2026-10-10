using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Parsimony;

/// <summary>The distinct recommendation axes covered by Parsimony review.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParsimonyAxis>))]
public enum ParsimonyAxis
{
    /// <summary>Reduce duplicated or unnecessarily complex grammar statements.</summary>
    [JsonStringEnumMemberName("parsimony")]
    Parsimony,

    /// <summary>Prefer the narrowest statement that preserves attested forms.</summary>
    [JsonStringEnumMemberName("restrictiveness")]
    Restrictiveness,

    /// <summary>Examine both parsimony and restrictiveness.</summary>
    [JsonStringEnumMemberName("both")]
    Both,
}

/// <summary>The strongest evidence tier a measure recommendation requires.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParsimonyTier>))]
public enum ParsimonyTier
{
    /// <summary>Grammar structure and loaded facts only.</summary>
    [JsonStringEnumMemberName("static")]
    Static,

    /// <summary>Grammar facts joined with attested forms and approved analyses.</summary>
    [JsonStringEnumMemberName("text")]
    Text,

    /// <summary>Completed parser outcomes over explicit cases.</summary>
    [JsonStringEnumMemberName("parser")]
    Parser,
}

/// <summary>The kind of identity a finding or disposition names.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParsimonyAttachmentKind>))]
public enum ParsimonyAttachmentKind
{
    /// <summary>The finding describes a whole project.</summary>
    [JsonStringEnumMemberName("project")]
    Project,

    /// <summary>The finding describes a named family or group.</summary>
    [JsonStringEnumMemberName("group")]
    Group,

    /// <summary>The finding names a typed authored grammar object.</summary>
    [JsonStringEnumMemberName("authored-object")]
    AuthoredObject,

    /// <summary>The finding names a word case.</summary>
    [JsonStringEnumMemberName("word-case")]
    WordCase,
}

/// <summary>The authored grammar object kinds that the measure catalog names.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParsimonyAuthoredObjectKind>))]
public enum ParsimonyAuthoredObjectKind
{
    /// <summary>An ad hoc prohibition.</summary>
    [JsonStringEnumMemberName("adhoc-prohibition")]
    AdhocProhibition,

    /// <summary>An inflectional morphological analysis.</summary>
    [JsonStringEnumMemberName("inflectional-msa")]
    InflectionalMsa,

    /// <summary>An affix template.</summary>
    [JsonStringEnumMemberName("affix-template")]
    AffixTemplate,

    /// <summary>An affix slot.</summary>
    [JsonStringEnumMemberName("affix-slot")]
    AffixSlot,

    /// <summary>An allomorph.</summary>
    [JsonStringEnumMemberName("allomorph")]
    Allomorph,

    /// <summary>A natural class.</summary>
    [JsonStringEnumMemberName("natural-class")]
    NaturalClass,

    /// <summary>An environment statement.</summary>
    [JsonStringEnumMemberName("environment")]
    Environment,
}

/// <summary>The group kinds that the measure catalog names.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParsimonyGroupKind>))]
public enum ParsimonyGroupKind
{
    /// <summary>A family of listed shapes that may share a sound rule.</summary>
    [JsonStringEnumMemberName("alternation-family")]
    AlternationFamily,

    /// <summary>A family of prohibitions that may state an affix order.</summary>
    [JsonStringEnumMemberName("adhoc-slot-order")]
    AdhocSlotOrder,

    /// <summary>A set of allomorph records with duplicate authored forms.</summary>
    [JsonStringEnumMemberName("allomorph-duplicate-forms")]
    AllomorphDuplicateForms,

    /// <summary>A set of authored environment or natural-class statements.</summary>
    [JsonStringEnumMemberName("unused-statement")]
    UnusedStatement,
}

/// <summary>The completeness of recipe-level before and after verification.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParsimonyVerification>))]
public enum ParsimonyVerification
{
    /// <summary>No paired verification has been run.</summary>
    [JsonStringEnumMemberName("not-run")]
    NotRun,

    /// <summary>The required comparisons completed and met the recipe criteria.</summary>
    [JsonStringEnumMemberName("passed")]
    Passed,

    /// <summary>A completed comparison failed a required recipe criterion.</summary>
    [JsonStringEnumMemberName("failed")]
    Failed,

    /// <summary>Required evidence or a complete comparison was unavailable.</summary>
    [JsonStringEnumMemberName("inconclusive")]
    Inconclusive,
}

/// <summary>How a finding's numeric trigger is expressed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParsimonyThresholdOperator>))]
public enum ParsimonyThresholdOperator
{
    /// <summary>The measured number must exceed the stated value.</summary>
    [JsonStringEnumMemberName(">")]
    GreaterThan,

    /// <summary>The measured number must be at least the stated value.</summary>
    [JsonStringEnumMemberName(">=")]
    GreaterThanOrEqual,

    /// <summary>The measured number must equal the stated value.</summary>
    [JsonStringEnumMemberName("=")]
    Equal,

    /// <summary>The finding is ranked without a numeric trigger.</summary>
    [JsonStringEnumMemberName("ranked-only")]
    RankedOnly,
}

/// <summary>The advisory response recorded for one exact finding and evidence digest.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParsimonyDispositionKind>))]
public enum ParsimonyDispositionKind
{
    /// <summary>Record intent to address the finding.</summary>
    [JsonStringEnumMemberName("fix")]
    Fix,

    /// <summary>Keep the grammar statement for the cited evidence.</summary>
    [JsonStringEnumMemberName("keep")]
    Keep,

    /// <summary>Record a question that still needs an answer.</summary>
    [JsonStringEnumMemberName("ask")]
    Ask,

    /// <summary>Leave the finding for later review.</summary>
    [JsonStringEnumMemberName("defer")]
    Defer,
}

/// <summary>Who recorded an advisory disposition.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParsimonyActorType>))]
public enum ParsimonyActorType
{
    /// <summary>A person recorded the disposition.</summary>
    [JsonStringEnumMemberName("human")]
    Human,

    /// <summary>An agent recorded the disposition.</summary>
    [JsonStringEnumMemberName("agent")]
    Agent,
}

/// <summary>The typed identity a finding concerns.</summary>
/// <param name="Kind">The identity category.</param>
/// <param name="Identity">The exact canonical object, group, case, or project key.</param>
/// <param name="AuthoredObjectKind">The object type when <paramref name="Kind"/> is <see cref="ParsimonyAttachmentKind.AuthoredObject"/>.</param>
/// <param name="GroupKind">The group type when <paramref name="Kind"/> is <see cref="ParsimonyAttachmentKind.Group"/>.</param>
public sealed record ParsimonyFindingAttachment(
    ParsimonyAttachmentKind Kind,
    string Identity,
    ParsimonyAuthoredObjectKind? AuthoredObjectKind = null,
    ParsimonyGroupKind? GroupKind = null);

/// <summary>A finding's numerator, denominator, and counted unit.</summary>
/// <param name="Numerator">The counted finding cases.</param>
/// <param name="Denominator">The eligible cases.</param>
/// <param name="Unit">The named unit used for both counts.</param>
public sealed record ParsimonyMeasureNumber(long Numerator, long Denominator, string Unit);

/// <summary>Separate identity and count denominators read from one evidence bundle.</summary>
/// <param name="Scope">The named evidence scope summarized below.</param>
/// <param name="ScopeAvailable">Whether the requested scope was captured completely.</param>
/// <param name="ProjectWordforms">Distinct FieldWorks wordforms in the project.</param>
/// <param name="ProjectJudgedWordforms">Distinct wordforms with an Approved or Disapproved analysis.</param>
/// <param name="ProjectApprovedReadings">Approved analysis rows across the project.</param>
/// <param name="ProjectDisapprovedReadings">Disapproved analysis rows across the project.</param>
/// <param name="ProjectCandidateAnalyses">Unknown analysis rows across the project.</param>
/// <param name="ProjectLexemes">Distinct entry identities in project Approved readings.</param>
/// <param name="ProjectTextOccurrences">Token occurrences in every captured Text.</param>
/// <param name="ScopeForms">Distinct normalized forms in the requested scope, or null when unavailable.</param>
/// <param name="ScopeWordforms">Distinct FieldWorks wordform identities in the requested scope, or null when unavailable.</param>
/// <param name="ScopeApprovedReadings">Distinct Approved analysis identities in the requested scope, or null when unavailable.</param>
/// <param name="ScopeLexemes">Distinct entry identities in scoped Approved readings, or null when unavailable.</param>
/// <param name="ScopeTextOccurrences">Token occurrences in the scope's selected Texts, or null when not applicable.</param>
public sealed record ParsimonyJoinQuality(
    string Scope,
    bool ScopeAvailable,
    long ProjectWordforms,
    long ProjectJudgedWordforms,
    long ProjectApprovedReadings,
    long ProjectDisapprovedReadings,
    long ProjectCandidateAnalyses,
    long ProjectLexemes,
    long ProjectTextOccurrences,
    long? ScopeForms,
    long? ScopeWordforms,
    long? ScopeApprovedReadings,
    long? ScopeLexemes,
    long? ScopeTextOccurrences);

/// <summary>The trigger or ranked-only policy used by one finding.</summary>
/// <param name="Operator">The closed comparison rule.</param>
/// <param name="Value">The threshold value, or <see langword="null"/> for ranked-only.</param>
/// <param name="Version">The stable version of the threshold policy.</param>
public sealed record ParsimonyMeasureThreshold(
    ParsimonyThresholdOperator Operator,
    double? Value,
    string Version);

/// <summary>The outcome of running one registered Parsimony measure.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParsimonyMeasureStatus>))]
public enum ParsimonyMeasureStatus
{
    /// <summary>The fixed query completed over its full eligible input.</summary>
    [JsonStringEnumMemberName("computed")]
    Computed,

    /// <summary>The required material was not requested for this Report.</summary>
    [JsonStringEnumMemberName("not-run")]
    NotRun,

    /// <summary>The artifact does not contain a required table, loader fact, or identity bridge.</summary>
    [JsonStringEnumMemberName("not-available")]
    NotAvailable,

    /// <summary>Some eligible source material cannot be attributed with enough identity quality.</summary>
    [JsonStringEnumMemberName("inconclusive")]
    Inconclusive,

    /// <summary>The fixed query failed while reading a validated artifact.</summary>
    [JsonStringEnumMemberName("failed")]
    Failed,
}

/// <summary>Full eligibility and trigger counts for one measure in a Parsimony Report.</summary>
/// <param name="MeasureId">The stable measure registration.</param>
/// <param name="Status">Whether the requested evidence was available and computed.</param>
/// <param name="EligibleItems">The full eligible item count, or null when unavailable.</param>
/// <param name="FindingItems">The full number of eligible items that triggered the recommendation.</param>
/// <param name="Unit">The counted item kind.</param>
/// <param name="Number">The numerator, denominator, and unit when computation completed.</param>
/// <param name="Rate">The numerator divided by the denominator, or null for an empty or unavailable denominator.</param>
/// <param name="Detail">A short reason when the query was unavailable or inconclusive.</param>
public sealed record ParsimonyMeasureRun(
    string MeasureId,
    ParsimonyMeasureStatus Status,
    long? EligibleItems,
    long? FindingItems,
    string Unit,
    ParsimonyMeasureNumber? Number,
    double? Rate,
    string? Detail);

/// <summary>An information line that a Parsimony Report shows without counting it as a finding or recommendation.</summary>
/// <param name="Kind">The closed note category: <c>not-checked</c> or <c>no-effect</c>.</param>
/// <param name="MeasureId">The measure the note concerns.</param>
/// <param name="Text">The user-facing sentence.</param>
/// <param name="SubjectGuid">The authored object the note names, when it names one.</param>
public sealed record ParsimonyNote(string Kind, string MeasureId, string Text, string? SubjectGuid);

/// <summary>A closed, typed selector for a fixed Parsimony view.</summary>
/// <param name="ObjectGuid">An authored object identity required by item views.</param>
/// <param name="CategoryGuid">A category identity used to narrow template-order.</param>
/// <param name="StatementKind">The statement type used by statement-usage: environment or natural-class.</param>
/// <param name="Scope">The evidence scope used by join-quality.</param>
/// <param name="CaseKey">An optional exact parser case key.</param>
/// <param name="ReportId">The exact stored Report used by finding and suppression views.</param>
/// <param name="MeasureId">An optional exact measure filter.</param>
/// <param name="Disposition">An optional keep, defer, fix, or ask filter.</param>
/// <param name="State">An optional current or historical finding state filter.</param>
/// <param name="Search">A bounded search over captions and reasons.</param>
/// <param name="SubjectKey">An exact authored object or computed group identity.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ParsimonyViewFilters(
    string? ObjectGuid = null,
    string? CategoryGuid = null,
    string? StatementKind = null,
    string? Scope = null,
    string? CaseKey = null,
    string? ReportId = null,
    string? MeasureId = null,
    string? Disposition = null,
    string? State = null,
    string? Search = null,
    string? SubjectKey = null);

/// <summary>A bounded request for a versioned fixed view over one bundle.</summary>
/// <param name="BundleId">The opaque bundle identity.</param>
/// <param name="View">The exact fixed view code.</param>
/// <param name="Filters">The closed set of typed filters.</param>
/// <param name="Limit">The page size, between 1 and 200.</param>
/// <param name="Cursor">A cursor issued for the same bundle, view, version, and filters.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ParsimonyNamedViewRequest(
    string BundleId,
    string View,
    ParsimonyViewFilters Filters,
    int Limit = 50,
    string? Cursor = null);

/// <summary>A decoded cursor bound to one exact named-view request.</summary>
/// <param name="BundleId">The opaque bundle identity.</param>
/// <param name="View">The exact fixed view code.</param>
/// <param name="Version">The public view schema version.</param>
/// <param name="FilterDigest">The digest of the canonical typed filters.</param>
/// <param name="Offset">The next row offset in the stable result ordering.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ParsimonyViewCursor(string BundleId, string View, int Version, string FilterDigest, int Offset);

/// <summary>The result of one fixed named-view page.</summary>
/// <param name="BundleId">The bundle whose artifacts were queried.</param>
/// <param name="View">The exact fixed view code.</param>
/// <param name="Version">The public view schema version.</param>
/// <param name="Status">Whether the view was computed or its source capability was unavailable.</param>
/// <param name="Capabilities">The exact fact sections this view needs.</param>
/// <param name="Total">The full eligible row count, or null when the query was not available.</param>
/// <param name="Returned">The number of rows in this page.</param>
/// <param name="NextCursor">The request-bound cursor for the next page.</param>
/// <param name="Truncated">Whether additional rows remain.</param>
/// <param name="Rows">The typed rows returned by the named view.</param>
/// <param name="ItemsDigest">The digest of the complete ordered result, independent of page size.</param>
/// <param name="Detail">A reason when the required material is not available.</param>
public sealed record ParsimonyNamedViewResponse(
    string BundleId,
    string View,
    int Version,
    ParsimonyMeasureStatus Status,
    IReadOnlyList<string> Capabilities,
    long? Total,
    int Returned,
    string? NextCursor,
    bool Truncated,
    IReadOnlyList<ParsimonyViewRow> Rows,
    string? ItemsDigest,
    string? Detail)
{
    /// <summary>The Active findings in the captured Report and judgment projection.</summary>
    public int? ActiveCount { get; init; }

    /// <summary>The findings whose captured evidence matches an applied keep or defer.</summary>
    public int? SuppressedCount { get; init; }

    /// <summary>The active findings whose older keep or defer names different evidence.</summary>
    public int? ResurfacedCount { get; init; }

    /// <summary>The captured judgment inputs that could not safely determine a disposition.</summary>
    public int? UnresolvedJudgmentCount { get; init; }

    /// <summary>The digest of the human-input projection joined to this view.</summary>
    public string? JudgmentProjectionDigest { get; init; }
}

/// <summary>The registered Parsimony measures and named views available in this build.</summary>
public sealed record ParsimonyMeasureCatalogResponse(
    int MeasureSetVersion,
    IReadOnlyList<MeasureDefinition> Measures,
    IReadOnlyList<ParsimonyViewDefinition> Views);

/// <summary>A typed row returned by a fixed Parsimony view.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ParsimonyJoinQualityViewRow), "join-quality")]
[JsonDerivedType(typeof(ParsimonyAllomorphViewRow), "allomorph")]
[JsonDerivedType(typeof(ParsimonyAlternationFamilyViewRow), "alternation-family")]
[JsonDerivedType(typeof(ParsimonyStatementUsageViewRow), "statement-usage")]
[JsonDerivedType(typeof(ParsimonyAdhocViewRow), "adhoc-prohibition")]
[JsonDerivedType(typeof(ParsimonyAffixViewRow), "affix")]
[JsonDerivedType(typeof(ParsimonyTemplateViewRow), "template")]
[JsonDerivedType(typeof(ParsimonyNaturalClassViewRow), "natural-class")]
[JsonDerivedType(typeof(ParsimonyEnvironmentExcessViewRow), "environment-excess")]
[JsonDerivedType(typeof(ParsimonyNaturalClassExcessViewRow), "natural-class-excess")]
[JsonDerivedType(typeof(ParsimonyParserCaseViewRow), "parser-case")]
[JsonDerivedType(typeof(ParsimonyApprovedMorphSequenceViewRow), "approved-morph-sequence")]
[JsonDerivedType(typeof(ParsimonySlotContextViewRow), "slot-context")]
[JsonDerivedType(typeof(ParsimonyFindingDispositionViewRow), "finding-disposition")]
[JsonDerivedType(typeof(ParsimonySuppressionHistoryViewRow), "suppression-history")]
[JsonDerivedType(typeof(ParsimonyUnresolvedJudgmentViewRow), "unresolved-judgment")]
[JsonDerivedType(typeof(ParsimonyUnslottedAffixViewRow), "unslotted-affix")]
[JsonDerivedType(typeof(ParsimonyNullOptionalViewRow), "null-optional")]
public abstract record ParsimonyViewRow;

/// <summary>A frozen detector candidate with its separately derived presentation membership.</summary>
public sealed record ParsimonyFindingDispositionViewRow(
    string? ReportId,
    string BaselineBundleId,
    string State,
    ParsimonyFinding Finding,
    string? Disposition,
    string? Reason,
    string? Question,
    string? JudgmentId,
    string? RevisionId,
    string? PreviousEvidenceDigest,
    string? JudgmentState = null,
    string? Issue = null,
    IReadOnlyList<ParsimonyDispositionHeadView>? CurrentHeads = null) : ParsimonyViewRow;

/// <summary>One current disposition revision and the digest required to name it as an expected head.</summary>
public sealed record ParsimonyDispositionHeadView(
    string JudgmentId,
    string RevisionId,
    string ContentDigest,
    string State);

/// <summary>One applied keep or defer, including its digest for exact-head revision checks.</summary>
public sealed record ParsimonySuppressionHistoryViewRow(
    string JudgmentId,
    string RevisionId,
    string RecordId,
    string ContentDigest,
    string MeasureId,
    string SubjectKey,
    string SubjectCaption,
    string MeasureCaption,
    string EvidenceDigest,
    string Disposition,
    string? Reason,
    string State,
    string? SourceReportId,
    string? ReportId,
    string? BaselineBundleDigest,
    string SourceProjectId,
    string? ProposalId,
    string? ActorKind,
    string? ActorId,
    string? ActorName,
    string? JudgedAtUtc) : ParsimonyViewRow;

/// <summary>One malformed, incomplete, or conflicting judgment input that cannot hide a finding.</summary>
public sealed record ParsimonyUnresolvedJudgmentViewRow(
    string RecordId,
    string? JudgmentId,
    string State,
    string Reason,
    string PhysicalDigest) : ParsimonyViewRow;

/// <summary>The Active and Suppressed presentation derived from one frozen Report and Baseline.</summary>
public sealed record ParsimonyDispositionProjection(
    string BundleId,
    string? ReportId,
    string? BaselineBundleDigest,
    string SourceProjectId,
    string JudgmentProjectionDigest,
    int ActiveCount,
    int SuppressedCount,
    int ResurfacedCount,
    int UnresolvedJudgmentCount,
    IReadOnlyList<ParsimonyFindingDispositionViewRow> Findings,
    IReadOnlyList<ParsimonySuppressionHistoryViewRow> History,
    IReadOnlyList<ParsimonyUnresolvedJudgmentViewRow> Unresolved);

/// <summary>A view row carrying the distinct project and requested-scope denominators.</summary>
public sealed record ParsimonyJoinQualityViewRow(ParsimonyJoinQuality Quality) : ParsimonyViewRow;

/// <summary>An allomorph and its captured forms, environments, and approved witnesses.</summary>
public sealed record ParsimonyAllomorphViewRow(
    string AllomorphGuid,
    string EntryGuid,
    int Ordinal,
    string MorphType,
    bool IsAbstract,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Forms,
    IReadOnlyList<ParsimonyEnvironmentReference> Environments,
    IReadOnlyList<ParsimonyApprovedWitness> ApprovedWitnesses) : ParsimonyViewRow;

/// <summary>A repeated single-segment change across independent morphemes.</summary>
/// <param name="FamilyKey">The stable canonical family identity.</param>
/// <param name="Description">A short phrase naming the change, context, and independent morpheme count.</param>
/// <param name="Lane">Whether the finding uses feature-weighted evidence or exact segment identity.</param>
/// <param name="Directed">Whether resolved conditions distinguish the input from the output.</param>
/// <param name="ContextKey">The normalized context identity used to group this family.</param>
/// <param name="GateKey">The normalized MSA gate shared by the family members.</param>
/// <param name="Side">The shared allomorph morph type.</param>
/// <param name="InputPhonemeGuids">The exact directed input phoneme identities, when directed.</param>
/// <param name="OutputPhonemeGuids">The exact directed output phoneme identities, when directed.</param>
/// <param name="ChangedFeatures">Feature assignments that differ between the changed segments.</param>
/// <param name="SharedFeatures">Feature assignments that agree between the changed segments.</param>
/// <param name="Members">The independent entries, forms, and alignments supporting the family.</param>
/// <param name="Limitations">Limits that qualify this static evidence.</param>
public sealed record ParsimonyAlternationFamilyViewRow(string FamilyKey, string Description, string Lane, bool Directed,
    string ContextKey, string GateKey, string Side, IReadOnlyList<string> InputPhonemeGuids,
    IReadOnlyList<string> OutputPhonemeGuids, IReadOnlyList<ParsimonyAlternationFeatureChange> ChangedFeatures,
    IReadOnlyList<ParsimonyAlternationFeatureChange> SharedFeatures,
    IReadOnlyList<ParsimonyAlternationMemberView> Members, IReadOnlyList<string> Limitations) : ParsimonyViewRow;

/// <summary>One changed or shared phonological feature value.</summary>
/// <param name="FeatureGuid">The exact feature identity.</param>
/// <param name="InputValueGuid">The input segment's closed feature value.</param>
/// <param name="OutputValueGuid">The output segment's closed feature value.</param>
public sealed record ParsimonyAlternationFeatureChange(string FeatureGuid, string InputValueGuid,
    string OutputValueGuid);

/// <summary>One independent entry and its aligned sibling allomorph forms.</summary>
/// <param name="EntryGuid">The exact independent morpheme identity.</param>
/// <param name="Description">A readable entry form when the facts artifact provides one.</param>
/// <param name="AllomorphGuids">The exact sibling allomorph identities used by the alignments.</param>
/// <param name="WritingSystems">The vernacular writing systems with usable forms.</param>
/// <param name="Forms">The aligned forms, retained as written in their writing systems.</param>
/// <param name="LocalContexts">The unchanged phoneme context around each edit.</param>
/// <param name="AlignmentEvidence">The minimum cost and feature differences for the alignments.</param>
public sealed record ParsimonyAlternationMemberView(string EntryGuid, string Description,
    IReadOnlyList<string> AllomorphGuids, IReadOnlyList<string> WritingSystems, IReadOnlyList<string> Forms,
    IReadOnlyList<string> LocalContexts, string AlignmentEvidence);

/// <summary>An authored reference from an allomorph to one environment.</summary>
public sealed record ParsimonyEnvironmentReference(string Role, int Ordinal, string EnvironmentGuid,
    bool? Compiled, string? Result);

/// <summary>An Approved analysis that contains one exact authored morph identity.</summary>
public sealed record ParsimonyApprovedWitness(string WordformGuid, string AnalysisGuid, int MorphOrdinal,
    IReadOnlyDictionary<string, string> Forms);

/// <summary>An environment or natural class with its authored references and final compiler disposition.</summary>
public sealed record ParsimonyStatementUsageViewRow(
    string StatementKind,
    string StatementGuid,
    string Name,
    long ReferenceCount,
    long? CompiledReferenceCount,
    bool? Loaded,
    string? LoadDisposition,
    string Classification) : ParsimonyViewRow;

/// <summary>A prohibition and its complete ordered target list.</summary>
public sealed record ParsimonyAdhocViewRow(ParsimonyAdhocProhibition Prohibition, bool GroupedFactsAvailable)
    : ParsimonyViewRow;

/// <summary>One authored flat prohibition with its final known loader state.</summary>
public sealed record ParsimonyAdhocProhibition(string Guid, string Kind, bool Disabled, string Adjacency,
    string PrimaryGuid, string PrimaryTargetKind, IReadOnlyList<ParsimonyAdhocTarget> Others, bool? Loaded);

/// <summary>One ordered conjunctive target of an authored prohibition.</summary>
public sealed record ParsimonyAdhocTarget(string Guid, string Kind);

/// <summary>An MSA and the authored affix context reachable from it.</summary>
public sealed record ParsimonyAffixViewRow(string MsaGuid, string EntryGuid, string MsaKind,
    IReadOnlyList<string> CategoryGuids, IReadOnlyList<string> SlotGuids,
    IReadOnlyList<ParsimonyApprovedWitness> ApprovedWitnesses) : ParsimonyViewRow;

/// <summary>A template with its ordered slots and their optionality.</summary>
public sealed record ParsimonyTemplateViewRow(string TemplateGuid, string CategoryGuid, string Name,
    bool Disabled, IReadOnlyList<ParsimonyTemplateSlot> Slots) : ParsimonyViewRow;

/// <summary>A slot's exact side, declared order, and optionality within one template.</summary>
public sealed record ParsimonyTemplateSlot(string SlotGuid, string Side, int Ordinal, int? CompiledOrder,
    bool Optional);

/// <summary>A natural class with listed/effective members and its direct references.</summary>
public sealed record ParsimonyNaturalClassViewRow(string NaturalClassGuid,
    [property: JsonPropertyName("classKind")] string Kind, string Name,
    IReadOnlyList<string> Members, IReadOnlyList<string> EffectiveMembers,
    long EnvironmentReferences, long PatternReferences, bool? Loaded, string? LoadDisposition) : ParsimonyViewRow;

/// <summary>A parser case with its Assessment origin, ordered analyses, and supported counter availability.</summary>
public sealed record ParsimonyParserCaseViewRow(string AssessmentId, string InvocationId, string SourceSha256,
    string ParserSha256, string CaseKey, string Surface, string SurfaceNfd, string? WritingSystem,
    string? WordformGuid, string Status, string Completion, string IdentityStatus, string? Reason,
    IReadOnlyList<ParsimonyParserAnalysisViewRow> Analyses,
    IReadOnlyList<ParsimonyReviewedNegativeParserCase> ReviewedNegatives,
    IReadOnlyDictionary<string, long?> Counters)
    : ParsimonyViewRow;

/// <summary>One reviewed negative whose exact Assessment case is shown in this view.</summary>
public sealed record ParsimonyReviewedNegativeParserCase(string CaseId, string RevisionId,
    string ContentDigest, string ExpectationStatus, bool Accepted,
    IReadOnlyList<int> AcceptedAnalysisOrdinals, string? Reason);

/// <summary>One ordered parser analysis, with its exact match to any captured Disapproved reading.</summary>
public sealed record ParsimonyParserAnalysisViewRow(int Ordinal, string Signature, string IdentityStatus,
    IReadOnlyList<string> DisapprovedAnalysisGuids, IReadOnlyList<ParsimonyParserMorphViewRow> Morphs);

/// <summary>One ordered parser morph with exact source identities or its captured guessed string.</summary>
public sealed record ParsimonyParserMorphViewRow(int Ordinal, string? FormGuid, string? MsaGuid,
    string? InflectionTypeGuid, string? GuessedStringNfd);
/// <summary>A bounded environment's licensed contexts beside its uniquely aligned Approved triggers.</summary>
public sealed record ParsimonyEnvironmentExcessViewRow(string AllomorphGuid, string OwnerKey, string MsaGuid,
    string Bucket, IReadOnlyList<string> EnvironmentGuids, IReadOnlyList<string> EnvironmentNames, string Side,
    IReadOnlyList<string> Universe, IReadOnlyList<string> Licensed, IReadOnlyList<string> Observed,
    IReadOnlyList<string> LicensedExtras, IReadOnlyList<string> MissingObserved, int ExcessDenominator,
    int WordTypes, int Stems,
    IReadOnlyList<string> Witnesses, IReadOnlyList<string> ExistingClassesCoveringObserved,
    IReadOnlyList<string> FeatureIntersection, IReadOnlyList<string> IntersectionExtension,
    IReadOnlyList<string> UnknownFeatureMembers, string ScopePolicy) : ParsimonyViewRow;

/// <summary>A natural class usage site with its effective extension and attested trigger sounds.</summary>
public sealed record ParsimonyNaturalClassExcessViewRow(string ClassGuid, string ClassName, string UsageSite,
    string EnvironmentGuid, string AllomorphGuid, string Side, IReadOnlyList<string> Universe,
    IReadOnlyList<string> Observed, IReadOnlyList<string> CurrentClassExtras,
    IReadOnlyList<string> MissingObserved, int ExcessDenominator,
    IReadOnlyList<string> FeatureIntersection, IReadOnlyList<string> IntersectionExtension,
    IReadOnlyList<string> UnknownFeatureMembers, int WordTypes, int Stems,
    IReadOnlyList<string> Witnesses, IReadOnlyList<string> UnknownClassMembers) : ParsimonyViewRow;

/// <summary>An Approved word and its ordered recorded morph sequence.</summary>
public sealed record ParsimonyApprovedMorphSequenceViewRow(string AnalysisGuid, string WordformGuid, string Wordform,
    IReadOnlyList<ParsimonyApprovedMorph> Morphs) : ParsimonyViewRow;

/// <summary>One recorded morpheme and its authored identities and available text.</summary>
public sealed record ParsimonyApprovedMorph(int Ordinal, string? MorphGuid, string? MsaGuid, string? EntryGuid,
    string? MorphType, string? MsaKind, IReadOnlyDictionary<string, string> Forms,
    IReadOnlyDictionary<string, string> Glosses);

/// <summary>An affix slot with its inflectional users and loaded template positions.</summary>
public sealed record ParsimonySlotContextViewRow(string SlotGuid, string Name, string CategoryGuid, bool Optional,
    IReadOnlyList<string> MsaGuids, IReadOnlyList<ParsimonyTemplateSlotUse> TemplateUses) : ParsimonyViewRow;

/// <summary>A template position that refers to a slot.</summary>
public sealed record ParsimonyTemplateSlotUse(string TemplateGuid, string TemplateName, string CategoryGuid,
    string Side, int Ordinal, int? CompiledOrder);

/// <summary>An unslotted inflectional MSA with its loaded forms and Approved position evidence.</summary>
/// <param name="MsaGuid">The exact MSA identity.</param>
/// <param name="EntryGuid">The owning entry identity.</param>
/// <param name="Description">A readable entry form and gloss when the facts provide them.</param>
/// <param name="CategoryGuids">The authored part-of-speech restrictions.</param>
/// <param name="Realizations">The ordinary affix allomorphs with final compiler order.</param>
/// <param name="ApprovedHitCount">The distinct Approved analyses that contain this MSA.</param>
/// <param name="ApprovedAnalysisGuids">The exact Approved analysis identities for sequence lookup.</param>
/// <param name="Positions">The witnessed side and signed distance from the root.</param>
/// <param name="Precedence">Direct same-side pair counts and any supported partial-order suggestion.</param>
/// <param name="Exclusions">Reasons that Approved analyses did not contribute position evidence.</param>
public sealed record ParsimonyUnslottedAffixViewRow(string MsaGuid, string EntryGuid, string Description,
    IReadOnlyList<string> CategoryGuids, IReadOnlyList<ParsimonyLoadedAffixRealization> Realizations,
    int ApprovedHitCount, IReadOnlyList<string> ApprovedAnalysisGuids,
    IReadOnlyList<ParsimonyAffixPositionCount> Positions,
    IReadOnlyList<ParsimonyAffixPrecedenceCount> Precedence,
    IReadOnlyList<string> Exclusions) : ParsimonyViewRow;

/// <summary>A loaded ordinary prefix or suffix realization of an inflectional MSA.</summary>
/// <param name="AllomorphGuid">The exact authored allomorph identity.</param>
/// <param name="MorphType">The loaded affix side.</param>
/// <param name="Forms">The source forms by writing system.</param>
public sealed record ParsimonyLoadedAffixRealization(string AllomorphGuid, string MorphType,
    IReadOnlyDictionary<string, string> Forms);

/// <summary>A root-relative position supported by distinct Approved words and stems.</summary>
/// <param name="Side">Whether the affix occurs before or after the root.</param>
/// <param name="SignedDistance">The signed number of morphs from the root.</param>
/// <param name="AnalysisCount">The number of contributing Approved analyses.</param>
/// <param name="WordTypeCount">The number of distinct wordforms.</param>
/// <param name="StemCount">The number of distinct root entries.</param>
public sealed record ParsimonyAffixPositionCount(string Side, int SignedDistance, int AnalysisCount,
    int WordTypeCount, int StemCount);

/// <summary>Direct Approved order counts for one canonical pair of affix MSAs.</summary>
/// <param name="Side">The shared prefix or suffix side.</param>
/// <param name="FirstMsaGuid">The first MSA in canonical identity order.</param>
/// <param name="FirstDescription">The readable first MSA name.</param>
/// <param name="SecondMsaGuid">The second MSA in canonical identity order.</param>
/// <param name="SecondDescription">The readable second MSA name.</param>
/// <param name="FirstBeforeSecond">The distinct analyses witnessing that direct order.</param>
/// <param name="SecondBeforeFirst">The distinct analyses witnessing the opposite order.</param>
/// <param name="ExcludedAnalyses">Analyses excluded because a repeated MSA made this pair ambiguous.</param>
/// <param name="WordTypeCount">The distinct wordforms supporting the uncontested direction.</param>
/// <param name="StemCount">The distinct roots supporting the uncontested direction.</param>
/// <param name="SuggestPartialOrder">Whether the support floor is met with no contrary witness.</param>
/// <param name="Witnesses">The exact word and analysis identities for both directions.</param>
public sealed record ParsimonyAffixPrecedenceCount(string Side, string FirstMsaGuid, string FirstDescription,
    string SecondMsaGuid, string SecondDescription, int FirstBeforeSecond, int SecondBeforeFirst,
    int ExcludedAnalyses, int WordTypeCount, int StemCount, bool SuggestPartialOrder,
    IReadOnlyList<ParsimonyAffixOrderWitness> Witnesses);

/// <summary>One Approved word supporting a direct affix order.</summary>
/// <param name="AnalysisGuid">The exact Approved analysis identity.</param>
/// <param name="WordformGuid">The exact wordform identity.</param>
/// <param name="Wordform">The readable wordform.</param>
/// <param name="StemEntryGuid">The root entry identity.</param>
/// <param name="FirstBeforeSecond">Whether the canonical first MSA precedes the second.</param>
public sealed record ParsimonyAffixOrderWitness(string AnalysisGuid, string WordformGuid, string Wordform,
    string StemEntryGuid, bool FirstBeforeSecond);

/// <summary>A zero-only inflectional MSA or an excluded zero-shaped compiler output.</summary>
/// <param name="MsaGuid">The exact MSA identity.</param>
/// <param name="EntryGuid">The owning entry identity.</param>
/// <param name="Description">A readable entry form and gloss when the facts provide them.</param>
/// <param name="Classification">The optional-slot, obligatory-slot, or exclusion classification.</param>
/// <param name="Realizations">The authored null-like forms, compile recognition, and final compiler states.</param>
/// <param name="Slots">The slot memberships and optionality.</param>
/// <param name="FeatureEffects">The MSA feature structures and roles.</param>
/// <param name="InflectionClasses">The MSA's class restrictions.</param>
/// <param name="ExceptionFeatures">The MSA's required exception-feature references.</param>
/// <param name="AllomorphConditions">The authored environment references on zero realizations.</param>
/// <param name="SenseGuids">The senses that refer to this MSA.</param>
/// <param name="AdhocProhibitionGuids">The ad hoc prohibitions that target this MSA.</param>
/// <param name="ApprovedAnalysisGuids">The Approved analyses that use this MSA.</param>
/// <param name="SyntheticNullCount">Compiler-generated null outputs excluded from authored candidates.</param>
/// <param name="IsLoadedZeroOnly">Whether the MSA has a loaded authored null marker and no loaded nonzero sibling.</param>
/// <param name="IsRemovableCandidate">Whether the realization is featureless and unreferenced in an optional slot.</param>
/// <param name="Exclusions">Reasons this row is excluded from the authored zero-only denominator.</param>
public sealed record ParsimonyNullOptionalViewRow(string MsaGuid, string EntryGuid, string Description,
    string Classification, IReadOnlyList<ParsimonyZeroRealizationFact> Realizations,
    IReadOnlyList<ParsimonyZeroSlotMembership> Slots, IReadOnlyList<ParsimonyMsaFeatureEffect> FeatureEffects,
    IReadOnlyList<string> InflectionClasses, IReadOnlyList<string> ExceptionFeatures,
    IReadOnlyList<string> AllomorphConditions, IReadOnlyList<string> SenseGuids,
    IReadOnlyList<string> AdhocProhibitionGuids, IReadOnlyList<string> ApprovedAnalysisGuids,
    int SyntheticNullCount, bool IsLoadedZeroOnly, bool IsRemovableCandidate,
    IReadOnlyList<string> Exclusions) : ParsimonyViewRow;

/// <summary>An authored affix form and its compiler disposition.</summary>
/// <param name="AllomorphGuid">The source allomorph identity, or null for a synthetic output.</param>
/// <param name="MorphType">The source morph type when one exists.</param>
/// <param name="Forms">The authored source forms by writing system.</param>
/// <param name="IsAbstract">Whether the authored allomorph is abstract.</param>
/// <param name="Loaded">Whether the exact source allomorph has a final ordinary output.</param>
/// <param name="LoaderReason">The final compiler reason when the source was not loaded.</param>
public sealed record ParsimonyZeroRealizationFact(string? AllomorphGuid, string? MorphType,
    IReadOnlyDictionary<string, string> Forms, bool IsAbstract, bool Loaded, string? LoaderReason)
{
    /// <summary>
    /// Whether the authored form is a supported null marker with a final ordinary compiled output.
    /// </summary>
    public bool CompilerRecognizedZero { get; init; }
}

/// <summary>A declared slot membership and its optionality.</summary>
/// <param name="SlotGuid">The exact slot identity.</param>
/// <param name="Name">The readable slot name.</param>
/// <param name="Optional">Whether the slot allows absence, or null when the slot is unavailable.</param>
public sealed record ParsimonyZeroSlotMembership(string SlotGuid, string Name, bool? Optional);

/// <summary>An MSA feature structure and the role that applies it.</summary>
/// <param name="Role">The feature-structure role.</param>
/// <param name="FeatureStructureJson">The exact canonical feature structure.</param>
public sealed record ParsimonyMsaFeatureEffect(string Role, string FeatureStructureJson);

/// <summary>A reference into one published evidence bundle's fixed named views.</summary>
/// <param name="BundleId">The opaque identifier of the evidence bundle.</param>
/// <param name="View">The fixed named-view code.</param>
/// <param name="Arguments">The typed argument object for that view.</param>
public sealed record ParsimonyEvidenceReference(string BundleId, string View, JsonElement Arguments);

/// <summary>One advisory Parsimony recommendation with its evidence and shared recipe reference.</summary>
/// <param name="FindingId">The stable identity of the attached item and measure.</param>
/// <param name="MeasureId">The stable inventory ID.</param>
/// <param name="Axis">The recommendation axis.</param>
/// <param name="Tier">The strongest evidence tier supporting this result.</param>
/// <param name="AttachesTo">The exact project, group, authored-object, or word-case identity.</param>
/// <param name="GroupKey">The stable family identity, when this item belongs to a group.</param>
/// <param name="Number">The measured numerator, denominator, and unit.</param>
/// <param name="Threshold">The versioned numeric or ranked-only trigger.</param>
/// <param name="EvidenceDigest">The digest of this finding's relevant evidence.</param>
/// <param name="EvidenceRefs">The named evidence views used to inspect the finding.</param>
/// <param name="RecipeLink">The shared Guide code for the five-step recipe.</param>
/// <param name="Limitations">Evidence or attribution limits that qualify this finding.</param>
/// <param name="Verification">Whether recipe-level paired verification was run and complete.</param>
public sealed record ParsimonyFinding(
    string FindingId,
    string MeasureId,
    ParsimonyAxis Axis,
    ParsimonyTier Tier,
    ParsimonyFindingAttachment AttachesTo,
    string? GroupKey,
    ParsimonyMeasureNumber Number,
    ParsimonyMeasureThreshold Threshold,
    string EvidenceDigest,
    IReadOnlyList<ParsimonyEvidenceReference> EvidenceRefs,
    string RecipeLink,
    IReadOnlyList<string> Limitations,
    ParsimonyVerification Verification)
{
    /// <summary>The names the stored facts give the items this finding concerns; empty when the facts name none.</summary>
    public IReadOnlyList<string> ItemNames { get; init; } = [];
}

/// <summary>A durable advisory disposition tied to one finding identity and evidence digest.</summary>
/// <param name="MeasureId">The stable inventory ID.</param>
/// <param name="ItemKind">The closed attachment category for the item.</param>
/// <param name="ItemKey">The normalized, non-null item identity.</param>
/// <param name="GroupKey">The normalized, non-null group identity used with the item.</param>
/// <param name="EvidenceDigest">The exact finding evidence this answer concerns.</param>
/// <param name="Disposition">The person's or agent's advisory response.</param>
/// <param name="Reason">The explanation for a keep or other optional rationale.</param>
/// <param name="PendingQuestion">The question retained when the disposition is ask.</param>
/// <param name="ActorType">Whether a person or an agent recorded the response.</param>
/// <param name="ActorId">The stable identity of its author.</param>
/// <param name="RecordedUtc">The time the response was recorded.</param>
/// <param name="Version">The optimistic concurrency version.</param>
/// <param name="SourceReportId">The Report that supplied the finding.</param>
/// <param name="ProposalId">The optional Draft or Proposal associated with a fix.</param>
public sealed record ParsimonyDispositionRecord(
    string MeasureId,
    ParsimonyAttachmentKind ItemKind,
    string ItemKey,
    string GroupKey,
    string EvidenceDigest,
    ParsimonyDispositionKind Disposition,
    string? Reason,
    string? PendingQuestion,
    ParsimonyActorType ActorType,
    string ActorId,
    DateTimeOffset RecordedUtc,
    long Version,
    string SourceReportId,
    string? ProposalId);
