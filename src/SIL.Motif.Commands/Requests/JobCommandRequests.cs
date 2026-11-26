using System;
using SIL.Motif.Contract.Assess;

namespace SIL.Motif.Commands.Requests;

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
public sealed record EnqueueBaselineRefreshRequest(string FwDataPath, string ProductVersion);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">The exact canonical Proposal identity.</param>
/// <param name="NoBackground">True queues no Parsimony Baseline bundle job when the Dry Run runs, so nothing commits after the command returns.</param>
public sealed record EnqueueDryRunRequest(string FwDataPath, string ProductVersion, string ProposalId, bool NoBackground = false);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">The exact canonical Proposal identity.</param>
/// <param name="Scope">The named Assessment scope, or the project default when omitted.</param>
/// <param name="Words">The explicit words to measure, or the configured Selection when omitted.</param>
/// <param name="AllWords">Whether to measure all project wordforms.</param>
/// <param name="ExpectedDraftRevision">The required Draft revision for this Trial.</param>
/// <param name="Limits">Optional per-word limits copied from the earlier Assessment being compared.</param>
/// <param name="BaselineAssessmentIds">The project Assessments used as the before side of a Trial Difference.</param>
/// <param name="NegativeWords">Attested counterexamples that should not parse.</param>
/// <param name="NoBackground">True runs any Baseline recapture this Trial needs in the foreground only: no Source index or Parsimony job is queued and no runner is woken, so nothing commits after the command returns.</param>
/// <param name="NoBaseSample">True measures exactly <paramref name="Words"/> and leaves out every configured review word set, so the Trial is a working-set Trial that cannot stand in for a gate; it needs Words.</param>
public sealed record EnqueueTrialRequest(
    string FwDataPath, string ProductVersion, string ProposalId, string? Scope = null,
    IReadOnlyList<string>? Words = null, bool AllWords = false, string? ExpectedDraftRevision = null,
    TrialLimits? Limits = null, IReadOnlyList<string>? BaselineAssessmentIds = null,
    IReadOnlyList<string>? NegativeWords = null, bool NoBackground = false, bool NoBaseSample = false)
{
    /// <summary>Words the author declares as intended losses before this Trial is queued.</summary>
    public IReadOnlyList<string>? ExpectedLosses { get; init; }

    /// <summary>
    /// Adds to <see cref="Words"/> every attested word whose Baseline analysis uses an object the Proposal edits;
    /// needs <see cref="Words"/>.
    /// </summary>
    public bool Touched { get; init; }

    /// <summary>
    /// Adds to <see cref="Words"/> corpus words that share a form the Proposal touches; needs <see cref="Words"/> and a
    /// corpus-types file. Null when no siblings were asked for.
    /// </summary>
    public SIL.Motif.Commands.Review.TrialSiblingsRequest? Siblings { get; init; }

    /// <summary>The siblings chosen for this Trial, frozen into its input.</summary>
    internal SIL.Motif.Commands.Review.TrialSiblingSample? SiblingSample { get; init; }

    /// <summary>The named review sets to include, or null to include every configured set.</summary>
    public IReadOnlyList<string>? ReviewSetIds { get; init; }

    /// <summary>When the CLI began work toward queueing this Trial, as a <see cref="System.Diagnostics.Stopwatch"/> timestamp; 0 before it began.</summary>
    internal long PrequeueStartedTimestamp { get; init; }

    /// <summary>What the before-side parse took and whether a stored result served it; null when none ran.</summary>
    internal SIL.Motif.Contract.Responses.TrialPhase? BeforeParsePhase { get; init; }

    /// <summary>Where the time limit came from when an earlier measurement of this Baseline set it; null when the policy's own limit was used.</summary>
    internal string? AdoptedNetDetail { get; init; }

    /// <summary>Starts the pre-queue clock once, at the first entry point a Trial passes through.</summary>
    internal EnqueueTrialRequest WithPrequeueClock() => PrequeueStartedTimestamp != 0 ? this
        : this with { PrequeueStartedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp() };

    internal SIL.Motif.Worker.Jobs.ReviewInputReference? ReviewInputs { get; init; }
    internal SIL.Motif.Worker.Store.ProposalRecord? ReviewProposal { get; init; }
    internal bool ReviewLeaseHeld { get; init; }
    internal SIL.Motif.Contract.Review.FrozenReviewInputs? ReviewSnapshot { get; init; }
    internal IReadOnlyList<SIL.Motif.Worker.Jobs.ReviewBaselinePartition>? ReviewBaselines { get; init; }
    internal string? ReviewBeforeStopCode { get; init; }
    internal IReadOnlyDictionary<string, string>? ReviewBeforeExpansions { get; init; }
    internal IReadOnlyList<SIL.Motif.Worker.Jobs.TrialBeforeDispatch>? ReviewBeforeDispatches { get; init; }
    internal IReadOnlyList<SIL.Motif.Worker.Jobs.TrialReviewObservation>? ReviewBeforePartial { get; init; }
}

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">The exact canonical Proposal identity.</param>
/// <param name="JobId">The exact job identity returned when evidence was queued.</param>
/// <param name="Timeout">The deadline for normal completion.</param>
/// <param name="WaitForTerminal">Cancel the job at the deadline and wait for its execution to end.</param>
public sealed record WaitForDryRunRequest(
    string FwDataPath, string ProductVersion, string ProposalId, string JobId, TimeSpan Timeout, bool WaitForTerminal = false);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="JobId">The exact job identity returned when evidence was queued.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="Timeout">The deadline for normal completion.</param>
/// <param name="WaitForTerminal">Cancel the job at the deadline and wait for its execution to end.</param>
public sealed record WaitForJobRequest(string FwDataPath, string JobId, string ProductVersion, TimeSpan Timeout, bool WaitForTerminal = false);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="JobId">The exact job identity returned when evidence was queued.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="Timeout">The deadline for normal completion.</param>
/// <param name="WaitForTerminal">Cancel the job at the deadline and wait for its execution to end.</param>
public sealed record JobsWaitRequest(string FwDataPath, string JobId, string ProductVersion, TimeSpan Timeout, bool WaitForTerminal = false);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="JobId">The exact job identity returned when evidence was queued.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
public sealed record ShowJobRequest(string FwDataPath, string JobId, string ProductVersion);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="JobId">The exact job identity returned when evidence was queued.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
public sealed record JobAssessmentsRequest(string FwDataPath, string JobId, string ProductVersion);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="JobId">The exact job identity returned when evidence was queued.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
public sealed record CancelJobRequest(string FwDataPath, string JobId, string ProductVersion);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="JobId">The exact job identity returned when evidence was queued.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
public sealed record RequeueJobRequest(string FwDataPath, string JobId, string ProductVersion);

/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
public sealed record ListActiveJobsRequest(string ProductVersion);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="JobId">The exact job identity returned when evidence was queued.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="Target">The exact canonical identity of the existing object.</param>
public sealed record MoveJobRequest(
    string FwDataPath, string JobId, string ProductVersion, JobMoveTarget Target);
