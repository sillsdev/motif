namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string JobWaitCancelled = "job.wait-cancelled";
    /// <summary>The supplied job is outside the wait's project, kind or Proposal.</summary>
    public const string JobWaitIdentityMismatch = "job.wait-identity-mismatch";
    /// <summary>The job kind has no execution lease for a terminal wait to confirm.</summary>
    public const string JobWaitKindUnsupported = "job.wait-kind-unsupported";
    public const string JobWaitCleanupUnconfirmed = "job.wait-cleanup-unconfirmed";
    public const string JobWaitTimeout = "job.wait-timeout";
    /// <summary>The job already finished, so there is nothing to cancel.</summary>
    public const string JobAlreadyFinished = "job.already-finished";
    /// <summary>A job recorded an Assessment that is missing from the project store.</summary>
    public const string JobAssessmentsInconsistent = "job.assessments-inconsistent";
    /// <summary>The Dry Run job finished without completing.</summary>
    public const string JobDryRunIncomplete = "job.dry-run-incomplete";
    /// <summary>A job id is missing or not valid.</summary>
    public const string JobInvalidId = "job.invalid-id";
    /// <summary>A job was asked to move before itself.</summary>
    public const string JobInvalidMove = "job.invalid-move";
    /// <summary>Trial words are blank or carry surrounding whitespace.</summary>
    public const string JobInvalidWords = "job.invalid-words";
    /// <summary>The job to move before is not an active job.</summary>
    public const string JobMoveTargetNotFound = "job.move-target-not-found";
    /// <summary>The parser a queued job was pinned to can no longer be started, so the job was failed.</summary>
    public const string JobParserUnavailable = "job.parser-unavailable";
    /// <summary>The job recorded no Assessments.</summary>
    public const string JobNoAssessments = "job.no-assessments";
    /// <summary>The job is still running, so it cannot be requeued.</summary>
    public const string JobNotFinished = "job.not-finished";
    /// <summary>The project records no job with that id.</summary>
    public const string JobNotFound = "job.not-found";
}
