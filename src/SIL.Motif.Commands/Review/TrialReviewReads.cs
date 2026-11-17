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
    /// first time, and attaches the gate a waiting caller gates on.
    /// </summary>
    /// <param name="projectPath">The project file.</param>
    /// <param name="productVersion">The running product version.</param>
    /// <param name="proposal">The proposal id.</param>
    /// <param name="job">The finished Trial job id.</param>
    /// <param name="expectedLosses">Words the author intends to lose; they are reported but do not fail the gate.</param>
    public static CommandOutcome<TrialReviewResult> ReadJob(
        string projectPath, string productVersion, string proposal, string job,
        IReadOnlyCollection<string>? expectedLosses = null) =>
        ProjectStoreCommand.Run<TrialReviewResult>(projectPath, productVersion, (database, project) =>
        {
            var commands = new TrialReviewCommands(database);
            var key = ProjectWorkspaceKey.Compute(project);
            var read = commands.Read(new ReviewReadRequest(key, proposal, JobId: job));
            if (read.Refusal?.Code == RefusalCodes.DifferenceNoBefore)
                read = commands.ProduceAsync(new ReviewProduceRequest(key, proposal, job, null)).GetAwaiter().GetResult();
            if (!read.Succeeded || read.Value is not AvailableTrialResult available) return read;
            return CommandOutcome<TrialReviewResult>.Success(available with
            {
                Gate = GateOf(available, expectedLosses ?? [], LostWords(commands, key, proposal, available)),
            });
        });

    /// <summary>
    /// The gate of a report: <c>unmeasured</c> when nothing or not everything was measured, otherwise
    /// <c>loss</c> when a lost word is not in <paramref name="expectedLosses"/>, otherwise <c>green</c>.
    /// </summary>
    /// <param name="result">The report to judge.</param>
    /// <param name="expectedLosses">Words the author intends to lose.</param>
    /// <param name="lostWords">The words that went from analysed to no analysis, when the report can list them.</param>
    public static TrialGate GateOf(AvailableTrialResult result, IReadOnlyCollection<string> expectedLosses,
        IReadOnlyList<string>? lostWords)
    {
        ArgumentNullException.ThrowIfNull(result);
        var lostCount = result.Report.Summary.Flags
            .Where(flag => flag.Id == ReviewFlagId.NewWordFailures).Sum(flag => flag.WordCount ?? 0);
        if (UnusableReason(result) is { } unusable)
            return new TrialGate(TrialGateState.Unmeasured, unusable, lostCount, [], []);
        if (lostCount == 0) return new TrialGate(TrialGateState.Green, null, 0, [], []);
        var expected = expectedLosses.Select(Key).ToHashSet(StringComparer.Ordinal);
        // A loss the report cannot name stays unexpected: only a listed word is excused.
        var listed = lostWords is { } words && words.Count >= lostCount ? words : null;
        var matched = listed?.Where(word => expected.Contains(Key(word))).Order(StringComparer.Ordinal).ToArray() ?? [];
        var unexpected = listed?.Where(word => !expected.Contains(Key(word))).Order(StringComparer.Ordinal).ToArray() ?? [];
        var unexpectedCount = listed is null ? lostCount : unexpected.Length;
        return unexpectedCount == 0
            ? new TrialGate(TrialGateState.Green, null, lostCount, matched, [])
            : new TrialGate(TrialGateState.Loss,
                unexpectedCount + " measured word(s) lost their analysis and were not listed as expected losses.",
                lostCount, matched, unexpected);
    }

    private static string Key(string word) => word.Normalize(System.Text.NormalizationForm.FormC);

    private static IReadOnlyList<string>? LostWords(TrialReviewCommands commands, string key, string proposal,
        AvailableTrialResult available)
    {
        var words = new List<string>();
        string? cursor = null;
        do
        {
            var page = commands.Read(new ReviewReadRequest(key, proposal, ReportId: available.Report.ReportId,
                Detail: DetailLevel.Full, Section: "wordforms", Flag: ReviewFlagId.NewWordFailures,
                Cursor: cursor, Limit: 200));
            if (!page.Succeeded || page.Value is not AvailableTrialResult { Report.Page: { } items }) return null;
            words.AddRange(items.Items.OfType<SIL.Motif.Contract.Review.WordRow>().Select(row => row.Word));
            cursor = items.NextCursor;
        } while (cursor is not null);
        return words;
    }

    /// <summary>
    /// Why a finished Trial's report cannot gate a script as a pass, or <see langword="null"/> when it measured
    /// words and every required case finished: nothing was requested, or evidence is incomplete or interrupted.
    /// </summary>
    public static string? UnusableReason(TrialReviewResult result)
    {
        if (result is not AvailableTrialResult { Report.Summary: { } summary }) return null;
        if (summary.Wordforms.Requested == 0) return "The Trial measured no words.";
        var notMeasured = (summary.Incomplete.Before.NotMeasured ?? 0) + (summary.Incomplete.After.NotMeasured ?? 0);
        if (summary.Incomplete.RunInterruptions.Count > 0 || notMeasured > 0)
            return "The Trial finished with incomplete evidence: " + notMeasured +
                   " required case(s) were not measured" +
                   (summary.Incomplete.RunInterruptions.Count > 0 ? ", and the run was interrupted." : ".");
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
