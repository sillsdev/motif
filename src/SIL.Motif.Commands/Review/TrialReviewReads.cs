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
            TrialRetirementPaging.Apply(
                new TrialReviewCommands(database).Read(request with { ProjectKey = ProjectWorkspaceKey.Compute(project) }), request.Page));
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
    /// first time, and attaches the gate a waiting caller gates on.
    /// </summary>
    /// <param name="projectPath">The project file.</param>
    /// <param name="productVersion">The running product version.</param>
    /// <param name="proposal">The proposal id.</param>
    /// <param name="job">The finished Trial job id.</param>
    public static CommandOutcome<TrialReviewResult> ReadJob(
        string projectPath, string productVersion, string proposal, string job) =>
        ProjectStoreCommand.Run<TrialReviewResult>(projectPath, productVersion, (database, project) =>
        {
            var commands = new TrialReviewCommands(database);
            var key = ProjectWorkspaceKey.Compute(project);
            var read = commands.Read(new ReviewReadRequest(key, proposal, JobId: job));
            if (read.Refusal?.Code == RefusalCodes.DifferenceNoBefore)
                read = commands.ProduceAsync(new ReviewProduceRequest(key, proposal, job, null)).GetAwaiter().GetResult();
            if (!read.Succeeded || read.Value is not AvailableTrialResult available) return read;
            return TrialRetirementPaging.Apply(CommandOutcome<TrialReviewResult>.Success(available with
            {
                Gate = available.Report.Gate ?? GateOf(available, [], ReadGateWords(commands, key, proposal, available)),
            }), null);
        });

    /// <summary>The words a report's rows name, when the report can list them.</summary>
    /// <param name="LostWords">Words that went from analysed to no analysis, or lost a confirmed reading.</param>
    /// <param name="StoppedWords">Words stopped by a budget or timeout that had an analysis in the Baseline.</param>
    public sealed record GateWords(IReadOnlyList<string>? LostWords, IReadOnlyList<string>? StoppedWords);

    /// <summary>Evaluates captured facts with the shared Trial gate policy.</summary>
    public static TrialGate GateOf(AvailableTrialResult result, IReadOnlyCollection<string> expectedLosses, GateWords words) =>
        TrialGateEvaluation.GateOf(result, expectedLosses, new(words.LostWords, words.StoppedWords));

    private static GateWords ReadGateWords(TrialReviewCommands commands, string key, string proposal,
        AvailableTrialResult available)
    {
        var lost = new List<string>();
        var lostListed = true;
        foreach (var flag in new[] { ReviewFlagId.NewWordFailures, ReviewFlagId.ConfirmedReadingsLost })
        {
            if (PageWords(commands, key, proposal, available, flag, null) is { } rows)
                lost.AddRange(rows.Select(row => row.Word));
            else lostListed = false;
        }
        var stopped = PageWords(commands, key, proposal, available, null, "incomplete")?
            .Where(row => row.After.Completion == Completion.Stopped && row.Before.ObservedReadingCount > 0)
            .Select(row => row.Word).ToArray();
        return new GateWords(lostListed ? lost : null, stopped);
    }

    private static IReadOnlyList<SIL.Motif.Contract.Review.WordRow>? PageWords(TrialReviewCommands commands, string key,
        string proposal, AvailableTrialResult available, ReviewFlagId? flag, string? filter)
    {
        var rows = new List<SIL.Motif.Contract.Review.WordRow>();
        string? cursor = null;
        do
        {
            var page = commands.Read(new ReviewReadRequest(key, proposal, ReportId: available.Report.ReportId,
                Detail: DetailLevel.Full, Section: "wordforms", Flag: flag, Filter: filter, Cursor: cursor, Limit: 200));
            if (!page.Succeeded || page.Value is not AvailableTrialResult { Report.Page: { } items }) return null;
            rows.AddRange(items.Items.OfType<SIL.Motif.Contract.Review.WordRow>());
            cursor = items.NextCursor;
        } while (cursor is not null);
        return rows;
    }

    /// <summary>The recorded incompleteness that prevents a Trial from establishing a green gate.</summary>
    public static string? UnusableReason(TrialReviewResult result) => TrialGateEvaluation.UnusableReason(result);

    /// <summary>
    /// Why a Trial job that ended without a finished measurement must not look green, or <see langword="null"/>
    /// for a completed job.
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
