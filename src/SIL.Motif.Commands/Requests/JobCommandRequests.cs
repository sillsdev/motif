using System;

namespace SIL.Motif.Commands.Requests;

public sealed record EnqueueBaselineRefreshRequest(string FwDataPath, string ProductVersion);

public sealed record EnqueueDryRunRequest(string FwDataPath, string ProductVersion, string ProposalId);

public sealed record EnqueueTrialRequest(
    string FwDataPath, string ProductVersion, string ProposalId, string? Scope = null);

public sealed record WaitForDryRunRequest(
    string FwDataPath, string ProductVersion, string ProposalId, string JobId, TimeSpan Timeout);

public sealed record WaitForJobRequest(string FwDataPath, string JobId, string ProductVersion, TimeSpan Timeout);

public sealed record ShowJobRequest(string FwDataPath, string JobId, string ProductVersion);

public sealed record JobAssessmentsRequest(string FwDataPath, string JobId, string ProductVersion);

public sealed record CancelJobRequest(string FwDataPath, string JobId, string ProductVersion);

public sealed record RequeueJobRequest(string FwDataPath, string JobId, string ProductVersion);

public sealed record ListActiveJobsRequest(string ProductVersion);

public sealed record MoveJobRequest(
    string FwDataPath, string JobId, string ProductVersion, JobMoveTarget Target);
