using System;
using SIL.Motif.Contract.Assess;

namespace SIL.Motif.Commands.Requests;

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
public sealed record EnqueueBaselineRefreshRequest(string FwDataPath, string ProductVersion);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">The exact canonical Proposal identity.</param>
public sealed record EnqueueDryRunRequest(string FwDataPath, string ProductVersion, string ProposalId);

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
public sealed record EnqueueTrialRequest(
    string FwDataPath, string ProductVersion, string ProposalId, string? Scope = null,
    IReadOnlyList<string>? Words = null, bool AllWords = false, string? ExpectedDraftRevision = null,
    TrialLimits? Limits = null, IReadOnlyList<string>? BaselineAssessmentIds = null,
    IReadOnlyList<string>? NegativeWords = null);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">The exact canonical Proposal identity.</param>
/// <param name="JobId">The exact job identity returned when evidence was queued.</param>
/// <param name="Timeout">The maximum wait for a result before returning progress.</param>
public sealed record WaitForDryRunRequest(
    string FwDataPath, string ProductVersion, string ProposalId, string JobId, TimeSpan Timeout);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="JobId">The exact job identity returned when evidence was queued.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="Timeout">The maximum wait for a result before returning progress.</param>
public sealed record WaitForJobRequest(string FwDataPath, string JobId, string ProductVersion, TimeSpan Timeout);

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
