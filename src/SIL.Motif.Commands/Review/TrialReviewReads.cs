using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Review;

/// <summary>
/// The stored-report reads the CLI and the MCP server share, so both surfaces return the same
/// <see cref="TrialReviewResult"/> for the same Trial.
/// </summary>
public static class TrialReviewReads
{
    /// <summary>Reads a saved report without creating or upgrading the project's store.</summary>
    public static CommandOutcome<TrialReviewResult> ReadStored(string projectPath, ReviewReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.ReadExisting(projectPath, Absent(request), (database, project) =>
            new TrialReviewCommands(database).Read(request with { ProjectKey = ProjectWorkspaceKey.Compute(project) }));
    }

    /// <summary>The outcome of a read when the project has no Motif store yet.</summary>
    public static CommandOutcome<TrialReviewResult> Absent(ReviewReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Validate() is { } invalid)
            return CommandOutcome<TrialReviewResult>.Refused(new Refusal(
                RefusalCodes.DifferenceInvalidPage, FailureReason.InvalidArgument, invalid));
        if (request.ReportId is not null)
            return CommandOutcome<TrialReviewResult>.Refused(new Refusal(
                RefusalCodes.DifferenceNoTrial, FailureReason.NotFound,
                request.ReportId.StartsWith("legacy/", StringComparison.Ordinal)
                    ? "The legacy Trial was not found for this Proposal." : "The report was not found for this Proposal."));
        return CommandOutcome<TrialReviewResult>.Success(new NoTrialResult(request.ProposalId,
            [new ReadTarget(TargetKind.Tool, "Start a Trial", null, "motif_trial",
                new ReadArguments(Proposal: request.ProposalId, Action: "run"))]));
    }

    /// <summary>
    /// Reads the summary report of a finished Trial job, producing it from the job's retained evidence the
    /// first time, which is what a waiting caller returns when the job completes.
    /// </summary>
    public static CommandOutcome<TrialReviewResult> ReadJob(
        string projectPath, string productVersion, string proposal, string job) =>
        ProjectStoreCommand.Run<TrialReviewResult>(projectPath, productVersion, (database, project) =>
        {
            var commands = new TrialReviewCommands(database);
            var key = ProjectWorkspaceKey.Compute(project);
            var read = commands.Read(new ReviewReadRequest(key, proposal, JobId: job));
            return read.Refusal?.Code == RefusalCodes.DifferenceNoBefore
                ? commands.ProduceAsync(new ReviewProduceRequest(key, proposal, job, null)).GetAwaiter().GetResult() : read;
        });

    /// <summary>
    /// Why a finished Trial's report cannot gate a script as a pass, or <see langword="null"/> when it measured
    /// words and every required case finished: nothing was requested, or evidence is incomplete or interrupted.
    /// </summary>
    public static string? UnusableReason(TrialReviewResult result)
    {
        if (result is not AvailableTrialResult { Report.Summary: { } summary }) return null;
        if (summary.Wordforms.Requested == 0) return "The Trial measured no words.";
        if (summary.Incomplete.RunInterruptions.Count > 0 || summary.Incomplete.RequiredCases > 0)
            return "The Trial finished with incomplete evidence: " + (summary.Incomplete.RequiredCases ?? 0) +
                   " required case(s) were not measured.";
        return null;
    }

    /// <summary>
    /// Why a Trial job that ended without a finished measurement must not look green, or <see langword="null"/>
    /// for a completed job. A parser killed by a signal ends the job as completed-with-assessment-failure.
    /// </summary>
    public static string? UnfinishedReason(JobStatusResponse job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Status is JobStatus.Completed or null ? null
            : "The Trial did not finish measuring: job " + job.JobId + " ended as " + JobStatusJson.ToWire(job.Status.Value) +
              (job.FailureCategory is { } category and not JobFailureCategory.None
                  ? " (" + JobFailureCategoryJson.ToWire(category) + ")" : "") +
              ". Read `motif jobs show` for the parser's failure detail.";
    }
}
