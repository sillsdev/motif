using System.Text.Json.Serialization;
using SIL.Motif.Contract.Jobs;

namespace SIL.Motif.Contract.Parsimony;

/// <summary>Whether Parsimony has measured a Proposal's candidate, as a person would read it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProposalParsimonyState>))]
public enum ProposalParsimonyState
{
    /// <summary>Parsimony cannot run for this project at all, for example because PanGloss is missing.</summary>
    [JsonStringEnumMemberName("not-set-up")]
    NotSetUp,

    /// <summary>No Trial has finished yet, or one finished and no Parsimony job exists for it.</summary>
    [JsonStringEnumMemberName("not-yet-run")]
    NotYetRun,

    /// <summary>A Parsimony job is waiting behind other work.</summary>
    [JsonStringEnumMemberName("queued")]
    Queued,

    /// <summary>A Parsimony job is running; <see cref="ProposalParsimonyJob.Stage"/> names the step.</summary>
    [JsonStringEnumMemberName("running")]
    Running,

    /// <summary>A finished run whose inputs are still the current ones.</summary>
    [JsonStringEnumMemberName("ready")]
    Ready,

    /// <summary>A finished run whose inputs have since changed; <see cref="ProposalParsimonyStatus.Stale"/> says how.</summary>
    [JsonStringEnumMemberName("stale")]
    Stale,

    /// <summary>The newest Parsimony job did not finish; <see cref="ProposalParsimonyStatus.Failure"/> says why.</summary>
    [JsonStringEnumMemberName("failed")]
    Failed,
}

/// <summary>Where one Parsimony group stands for a Proposal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProposalParsimonyGroupState>))]
public enum ProposalParsimonyGroupState
{
    /// <summary>The group needs something the project has not set up, such as a Parsimony Selection.</summary>
    [JsonStringEnumMemberName("not-set-up")]
    NotSetUp,

    /// <summary>The group has not been measured for the current inputs.</summary>
    [JsonStringEnumMemberName("not-yet-run")]
    NotYetRun,

    /// <summary>At least one measure of the group computed.</summary>
    [JsonStringEnumMemberName("ready")]
    Ready,

    /// <summary>No measure of the group computed: each was not available or inconclusive, so nothing was judged.</summary>
    [JsonStringEnumMemberName("abstained")]
    Abstained,

    /// <summary>The group's measures failed while reading validated evidence.</summary>
    [JsonStringEnumMemberName("failed")]
    Failed,
}

/// <summary>Which input of a finished run has changed since the run.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProposalParsimonyStaleReason>))]
public enum ProposalParsimonyStaleReason
{
    /// <summary>The Baseline the candidate was measured against is no longer the current one.</summary>
    [JsonStringEnumMemberName("baseline-changed")]
    BaselineChanged,

    /// <summary>The Proposal's own content or intent changed.</summary>
    [JsonStringEnumMemberName("proposal-changed")]
    ProposalChanged,

    /// <summary>A prerequisite Proposal changed, or the prerequisite list did.</summary>
    [JsonStringEnumMemberName("prerequisite-changed")]
    PrerequisiteChanged,

    /// <summary>The PanGloss executable is not the one that measured.</summary>
    [JsonStringEnumMemberName("parser-changed")]
    ParserChanged,

    /// <summary>The facts or evidence file layout changed.</summary>
    [JsonStringEnumMemberName("schema-changed")]
    SchemaChanged,

    /// <summary>The measure catalog or the measures a group runs changed.</summary>
    [JsonStringEnumMemberName("measures-changed")]
    MeasuresChanged,

    /// <summary>The Parsimony Selection changed.</summary>
    [JsonStringEnumMemberName("selection-changed")]
    SelectionChanged,

    /// <summary>
    /// A human judgment that changes evidence changed. Reading it needs the project, so a status query does not
    /// report it; a run records it in the candidate's identity.
    /// </summary>
    [JsonStringEnumMemberName("judgments-changed")]
    JudgmentsChanged,
}

/// <summary>Why a Parsimony job did not finish, in a closed set a window can word.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProposalParsimonyFailureCategory>))]
public enum ProposalParsimonyFailureCategory
{
    /// <summary>PanGloss ran past its time limit, which a very large grammar does.</summary>
    [JsonStringEnumMemberName("timed-out")]
    TimedOut,

    /// <summary>The PanGloss executable was missing or was replaced during the run.</summary>
    [JsonStringEnumMemberName("parser-unavailable")]
    ParserUnavailable,

    /// <summary>PanGloss refused or failed while reading the grammar.</summary>
    [JsonStringEnumMemberName("facts-failed")]
    FactsFailed,

    /// <summary>Motif could not build or store the evidence for the candidate.</summary>
    [JsonStringEnumMemberName("evidence-failed")]
    EvidenceFailed,

    /// <summary>The job was cancelled before it finished.</summary>
    [JsonStringEnumMemberName("cancelled")]
    Cancelled,

    /// <summary>Anything else; the text gives what is known.</summary>
    [JsonStringEnumMemberName("internal")]
    Internal,
}

/// <summary>A closed reason code with one plain sentence.</summary>
/// <param name="Code">A stable code a window or agent can branch on.</param>
/// <param name="Text">One plain sentence; the code carries the meaning.</param>
public sealed record ProposalParsimonyReason(string Code, string Text);

/// <summary>The Parsimony job a status describes.</summary>
/// <param name="JobId">The job's id.</param>
/// <param name="Status">The job's wire status, such as <c>queued</c>, <c>running</c> or <c>completed</c>.</param>
/// <param name="Stage">The step a running job is in, or null.</param>
/// <param name="QueuedUtc">When the job was queued.</param>
/// <param name="FinishedUtc">When it reached a terminal status, or null.</param>
/// <param name="QueuedBehind"><c>trial</c> or <c>parse</c> when other work holds the queue, otherwise null.</param>
/// <param name="StageTimings">Wall time per stage of a finished candidate job, ending with <c>total</c>, or null.</param>
public sealed record ProposalParsimonyJob(string JobId, string Status, JobStageProgress? Stage,
    string QueuedUtc, string? FinishedUtc, string? QueuedBehind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ParsimonyStageTiming>? StageTimings = null);

/// <summary>What changed since a finished run.</summary>
/// <param name="Reasons">Every input that differs, in a fixed order.</param>
/// <param name="ReportKey">The inputs key the finished run recorded.</param>
/// <param name="CurrentKey">The inputs key the same Proposal has now.</param>
public sealed record ProposalParsimonyStale(IReadOnlyList<ProposalParsimonyStaleReason> Reasons,
    string ReportKey, string CurrentKey);

/// <summary>Why the newest job failed.</summary>
/// <param name="Category">The closed category.</param>
/// <param name="Text">One plain sentence a person can read.</param>
/// <param name="Retryable">Whether running it again could succeed without anything changing.</param>
/// <param name="WhenUtc">When the job stopped.</param>
public sealed record ProposalParsimonyFailure(ProposalParsimonyFailureCategory Category, string Text,
    bool Retryable, string WhenUtc);

/// <summary>The stored Reports a group was measured into.</summary>
/// <param name="BeforeBundleId">The bundle of the Baseline side.</param>
/// <param name="AfterBundleId">The bundle of the candidate side.</param>
/// <param name="CandidateIdentity">The candidate's identity digest.</param>
public sealed record ProposalParsimonyCandidate(string BeforeBundleId, string AfterBundleId,
    string CandidateIdentity);

/// <summary>How many findings one measure gained, lost or kept when the Proposal was added.</summary>
/// <param name="MeasureId">The measure.</param>
/// <param name="NewFindings">Findings only the candidate has.</param>
/// <param name="ResolvedFindings">Findings only the Baseline had.</param>
/// <param name="UnchangedFindings">Findings both sides have with the same evidence.</param>
/// <param name="EvidenceChangedFindings">Findings both sides have whose evidence differs.</param>
/// <param name="BeforeReportId">The Baseline side's stored Report.</param>
/// <param name="AfterReportId">The candidate side's stored Report.</param>
public sealed record ProposalParsimonyMeasureChange(string MeasureId, int NewFindings, int ResolvedFindings,
    int UnchangedFindings, int EvidenceChangedFindings, string BeforeReportId, string AfterReportId);

/// <summary>One Parsimony group's part of a Proposal's status.</summary>
/// <param name="Group">The group, <c>grammar</c> or <c>text</c>.</param>
/// <param name="State">Where the group stands.</param>
/// <param name="Note">A sentence about the group, such as how to set up its Texts, or null.</param>
/// <param name="Candidate">The stored bundles when the group was measured, otherwise null.</param>
/// <param name="MeasureRuns">What each measure of the group did on the candidate side.</param>
/// <param name="Changes">Per measure, never summed across measures.</param>
public sealed record ProposalParsimonyGroup(ParsimonyMeasureGroup Group, ProposalParsimonyGroupState State,
    string? Note, ProposalParsimonyCandidate? Candidate, IReadOnlyList<ParsimonyMeasureRun> MeasureRuns,
    IReadOnlyList<ProposalParsimonyMeasureChange> Changes);

/// <summary>
/// Whether Parsimony has measured a Proposal's candidate, and what it found, for one Proposal. It is advisory and
/// has no single score and no per-group score (ADR 0060 and ADR 0080): a failure, timeout or cancellation here never
/// changes the Trial, Readiness or Apply. Findings are fetched with the existing Report commands.
/// </summary>
/// <param name="SchemaVersion">Always 1.</param>
/// <param name="ProposalId">The Proposal this status is about.</param>
/// <param name="IntentDigest">The Proposal's intent digest when a Trial has been read, otherwise null.</param>
/// <param name="TrialJobId">The Trial whose candidate Parsimony measured, or null.</param>
/// <param name="State">The overall state.</param>
/// <param name="StateReason">A code and sentence for the state when it has a cause, otherwise null.</param>
/// <param name="Job">The newest Parsimony job for the Proposal, or null when none exists.</param>
/// <param name="Stale">What changed, only when <paramref name="State"/> is stale.</param>
/// <param name="Failure">Why it failed, only when <paramref name="State"/> is failed.</param>
/// <param name="Groups">The Grammar group, then the Text group.</param>
/// <param name="Advisory">Always true: Parsimony never gates the Trial, Readiness or Apply.</param>
public sealed record ProposalParsimonyStatus(
    int SchemaVersion,
    string ProposalId,
    string? IntentDigest,
    string? TrialJobId,
    ProposalParsimonyState State,
    ProposalParsimonyReason? StateReason,
    ProposalParsimonyJob? Job,
    ProposalParsimonyStale? Stale,
    ProposalParsimonyFailure? Failure,
    IReadOnlyList<ProposalParsimonyGroup> Groups,
    bool Advisory = true)
{
    /// <summary>The schema version every status carries.</summary>
    public const int CurrentSchemaVersion = 1;
}
