using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Runtime.Review;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Review;

/// <summary>Reads and explicitly produces the shared report for one Trial.</summary>
public sealed class TrialReviewCommands(MotifDatabase database)
{
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Reads frozen report facts without starting a parser or opening a live project.</summary>
    public CommandOutcome<TrialReviewResult> Read(ReviewReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Validate() is { } problem) return Invalid(problem);
        if (request.Trace) return Invalid("Trace requires the explicit trace command.");
        if (request.ReportId?.StartsWith("legacy/", StringComparison.Ordinal) == true)
        {
            var legacyJob = SelectJob(request.ProjectKey, request.ProposalId, request.ReportId[7..]);
            var projection = legacyJob is null ? null : LegacyTrialReviewProjection.Read(legacyJob);
            return projection is null ? Refused(RefusalCodes.DifferenceNoTrial, FailureReason.NotFound,
                "The legacy Trial was not found for this Proposal.") :
                CommandOutcome<TrialReviewResult>.Success(new AvailableTrialResult(projection with
                { Detail = request.EffectiveDetail, Freshness = Freshness(projection) }));
        }
        var job = request.ReportId is null ? SelectJob(request.ProjectKey, request.ProposalId, request.JobId) : null;
        if (request.ReportId is null && job is null)
            return CommandOutcome<TrialReviewResult>.Success(NoTrial(request.ProposalId));
        if (job is not null && !JobStateMachine.IsTerminal(job.Status))
            return CommandOutcome<TrialReviewResult>.Success(new PendingTrialResult(request.ProposalId, job.JobId,
                job.Status.ToString(), null, []));
        var reportId = request.ReportId ?? LatestReport(request.ProjectKey, request.ProposalId, job!.JobId);
        if (reportId is null)
        {
            var legacy = LegacyTrialReviewProjection.Read(job!);
            if (legacy is not null)
                return CommandOutcome<TrialReviewResult>.Success(new AvailableTrialResult(legacy with
                { Detail = request.EffectiveDetail, Freshness = Freshness(legacy) }));
            return Refused(RefusalCodes.DifferenceNoBefore, FailureReason.Refused,
                "The selected Trial has no retained review report; produce one from its captured evidence.");
        }
        var repository = new ReviewReportRepository(_database);
        var stored = repository.ReadManifest(new ReviewReportId(reportId));
        if (stored is null || stored.Summary.Pins.ProjectKey != request.ProjectKey ||
            stored.Summary.Pins.ProposalId != request.ProposalId)
            return Refused(RefusalCodes.DifferenceNoTrial, FailureReason.NotFound, "The report was not found for this Proposal.");
        var report = stored.Summary with { Detail = request.EffectiveDetail, Freshness = Freshness(stored.Summary) };
        if (request.EffectiveDetail == DetailLevel.Failures)
        {
            try { report = report with { Sections = FailureSections(repository, report, request.Section) }; }
            catch (ReviewDetailUnavailableException) { }
            catch (ArgumentException exception) { return Invalid(exception.Message); }
        }
        if (request.EffectiveDetail == DetailLevel.Full)
        {
            try
            {
                if (request.Word is not null || request.Case is not null)
                {
                    var cases = repository.FindCases(request.ProjectKey, new ReviewReportId(reportId), request.Word, request.Case);
                    if (cases.Count == 0) return Refused(RefusalCodes.DifferenceInvalidWord, FailureReason.InvalidArgument, "No matching case was found.");
                    if (cases.Count != 1) return Invalid("The word matches multiple cases: " + string.Join(", ", cases));
                    report = report with { Word = repository.ReadWord(request.ProjectKey, new ReviewReportId(reportId), cases[0], request.Limit ?? 50) };
                }
                else
                {
                    var page = repository.ReadPage(new ReviewPageRequest(request.ProjectKey, new ReviewReportId(reportId),
                        request.Section ?? "wordforms", request.Set, request.Outcome, request.Flag, request.Filter,
                        request.Cursor, request.Limit ?? 50));
                    report = report with { Page = page };
                }
            }
            catch (ReviewDetailUnavailableException) { return Refused(RefusalCodes.DifferenceNoBefore, FailureReason.Refused, "Report detail is no longer retained."); }
            catch (ArgumentException exception) { return Invalid(exception.Message); }
        }
        return CommandOutcome<TrialReviewResult>.Success(new AvailableTrialResult(report));
    }

    /// <summary>Produces a report explicitly from the selected retained Trial.</summary>
    public async Task<CommandOutcome<TrialReviewResult>> ProduceAsync(ReviewProduceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Validate() is { } problem) return Invalid(problem);
        var jobId = request.JobId;
        if (request.ReportId is not null)
        {
            var old = new ReviewReportRepository(_database).ReadManifest(new ReviewReportId(request.ReportId));
            if (old is null || old.Summary.Pins.ProjectKey != request.ProjectKey || old.Summary.Pins.ProposalId != request.ProposalId)
                return Refused(RefusalCodes.DifferenceNoTrial, FailureReason.NotFound, "The report was not found for this Proposal.");
            jobId = old.Summary.Pins.TrialJobId;
        }
        var job = SelectJob(request.ProjectKey, request.ProposalId, jobId);
        if (job is null) return CommandOutcome<TrialReviewResult>.Success(NoTrial(request.ProposalId));
        if (!JobStateMachine.IsTerminal(job.Status))
            return CommandOutcome<TrialReviewResult>.Success(new PendingTrialResult(request.ProposalId, job.JobId, job.Status.ToString(), null, []));
        try
        {
            var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(job.JobId), ct).ConfigureAwait(false);
            return Read(new ReviewReadRequest(request.ProjectKey, request.ProposalId, ReportId: id.Value));
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or KeyNotFoundException)
        { return Refused(RefusalCodes.DifferenceUnreadable, FailureReason.StoreInconsistent, exception.Message); }
    }

    /// <summary>Reads one indexed page within the exact saved report.</summary>
    public CommandOutcome<TrialReviewResult> Page(ReviewPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Validate() is { } problem) return Invalid(problem);
        var stored = new ReviewReportRepository(_database).ReadManifest(request.ReportId);
        if (stored is null || stored.Summary.Pins.ProjectKey != request.ProjectKey)
            return Refused(RefusalCodes.DifferenceNoTrial, FailureReason.NotFound, "The report was not found.");
        return Read(new ReviewReadRequest(request.ProjectKey, stored.Summary.Pins.ProposalId, ReportId: request.ReportId.Value,
            Detail: DetailLevel.Full, Section: request.Section, Set: request.Set, Outcome: request.Outcome,
            Flag: request.Flag, Filter: request.Filter, Cursor: request.Cursor, Limit: request.Limit));
    }

    private static IReadOnlyList<SectionView> FailureSections(ReviewReportRepository repository, ReportView report, string? selected)
    {
        var groups = new (string Section, string Label, string Cause, string Outcome)[]
        {
            ("wordforms", "Wordforms and analyses", "no-parse", "new-no-parse"),
            ("wordforms", "Wordforms and analyses", "approved-reading-lost", "approved-reading-lost"),
            ("controls", "Reference and regression controls", "reference-reading-lost", "reference-reading-lost"),
            ("negative-controls", "Negative controls", "negative-now-parsing", "negative-now-parsing"),
            ("incomplete", "Incomplete", "incomplete/work-limit", "incomplete/work-limit"),
            ("incomplete", "Incomplete", "incomplete/time-limit", "incomplete/time-limit"),
            ("incomplete", "Incomplete", "incomplete/not-measured", "incomplete/not-measured"),
            ("incomplete", "Incomplete", "incomplete/invalid-evidence", "incomplete/invalid-evidence"),
        };
        var sections = new List<SectionView>();
        var remaining = 30;
        foreach (var section in groups.Where(group => selected is null || group.Section == selected).GroupBy(group => (group.Section, group.Label)))
        {
            var causes = new List<CauseGroup>();
            long? total = null;
            foreach (var group in section)
            {
                var page = repository.ReadPage(new ReviewPageRequest(report.Pins.ProjectKey, new ReviewReportId(report.ReportId),
                    group.Section, Outcome: group.Outcome, Limit: Math.Max(1, Math.Min(5, remaining))));
                total = page.TotalInSection;
                var count = page.TotalMatching;
                if (count == 0) continue;
                var samples = repository.ReadFailureSamples(new ReviewPageRequest(report.Pins.ProjectKey,
                    new ReviewReportId(report.ReportId), group.Section, Outcome: group.Outcome), Math.Min(5, remaining));
                remaining -= samples.Count;
                var target = new ReadTarget(TargetKind.Tool, group.Cause, null, "motif_difference",
                    new ReadArguments(Proposal: report.Pins.ProposalId, Report: report.ReportId, Detail: DetailLevel.Full,
                        Section: group.Section, Outcome: group.Outcome));
                causes.Add(new CauseGroup(group.Cause, count, page.Unit, samples,
                    count is null ? null : Math.Max(0, count.Value - samples.Count), target));
            }
            if (causes.Count > 0 || selected is not null)
                sections.Add(new SectionView(section.Key.Section, section.Key.Label, "Stored Trial evidence", report.Pins.EvidenceManifestDigest,
                    new SectionCounts("cases", total, null, null), causes, [], []));
        }
        foreach (var section in new[] { "warnings", "parsimony" }.Where(section => selected is null || section == selected))
        {
            IReadOnlyList<(string Group, long Count)> eventGroups;
            try { eventGroups = repository.ReadFailureEventGroups(report.Pins.ProjectKey, new ReviewReportId(report.ReportId), section); }
            catch (ReviewDetailUnavailableException) { continue; }
            var causes = new List<CauseGroup>();
            foreach (var (group, count) in eventGroups)
            {
                var outcome = (section == "warnings" ? "warning/" : "parsimony/") + group;
                var request = new ReviewPageRequest(report.Pins.ProjectKey, new ReviewReportId(report.ReportId), section,
                    Outcome: outcome, Flag: ReviewFlagId.NewWarningsOrParsimony, Limit: Math.Max(1, Math.Min(5, remaining)));
                var page = repository.ReadPage(request);
                var samples = remaining == 0 ? [] : page.Items.Take(remaining).ToArray();
                remaining -= samples.Length;
                var target = new ReadTarget(TargetKind.Tool, outcome, null, "motif_difference",
                    new ReadArguments(Proposal: report.Pins.ProposalId, Report: report.ReportId, Detail: DetailLevel.Full,
                        Section: section, Outcome: outcome, Flag: ReviewFlagId.NewWarningsOrParsimony));
                causes.Add(new CauseGroup(outcome, count, page.Unit, samples, count - samples.Length, target));
            }
            if (causes.Count > 0 || selected is not null)
                sections.Add(new SectionView(section, section == "warnings" ? "Warnings" : "Parsimony", "Stored Trial evidence",
                    report.Pins.EvidenceManifestDigest, new SectionCounts(section == "warnings" ? "diagnostics" : "findings",
                        eventGroups.Sum(group => group.Count), null, null), causes, [], []));
        }
        return sections;
    }

    private SIL.Motif.Contract.Jobs.JobRecord? SelectJob(string project, string proposal, string? id)
    {
        var jobs = new JobRepository(_database).ListByProjectAndKind(project, TrialJobHandler.TrialKind)
            .Where(job => id is null || job.JobId == id).OrderByDescending(job => job.CreatedUtc, StringComparer.Ordinal)
            .ThenByDescending(job => job.JobId, StringComparer.Ordinal);
        foreach (var job in jobs.OrderBy(job => id is null && JobStateMachine.IsTerminal(job.Status) &&
            job.Status != SIL.Motif.Contract.Jobs.JobStatus.Completed ? 1 : 0))
        {
            var input = TrialJobInput.Parse(job.InputJson);
            if (ProposalJsonParser.Parse(input.ProposalJson).ProposalId.Value == proposal) return job;
        }
        return null;
    }

    private string? LatestReport(string project, string proposal, string job)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ReportId FROM ReviewReports WHERE ProjectKey=$project AND ProposalId=$proposal AND TrialJobId=$job ORDER BY CreatedUtc DESC,ReportId DESC LIMIT 1;";
        command.Parameters.AddWithValue("$project", project);
        command.Parameters.AddWithValue("$proposal", proposal);
        command.Parameters.AddWithValue("$job", job);
        return command.ExecuteScalar() as string;
    }

    private Freshness Freshness(ReportView report)
    {
        var reasons = new List<string>();
        var unknown = false;
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT CurrentIntentDigest,DraftName,DraftJson FROM Proposals WHERE ProposalId=$id;";
            var id = command.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Text);
            string? Current(string proposalId)
            {
                id.Value = proposalId;
                using var reader = command.ExecuteReader();
                if (!reader.Read()) return null;
                if (reader.IsDBNull(1)) return reader.IsDBNull(0) ? null : reader.GetString(0);
                if (reader.IsDBNull(2)) return null;
                try { return SIL.Motif.Contract.Canonicalization.IntentDigest.Compute(ProposalJsonParser.Parse(reader.GetString(2))); }
                catch (Exception exception) when (exception is SIL.Motif.Contract.Parsing.ContractParseException or ArgumentException)
                { return null; }
            }
            var current = Current(report.Pins.ProposalId);
            if (current is null) unknown = true;
            else if (current != report.Pins.IntentDigest) reasons.Add("proposal-changed");
            foreach (var prerequisite in report.Pins.Prerequisites)
            {
                current = Current(prerequisite.ProposalId);
                if (current is null) reasons.Add("prerequisite-unavailable");
                else if (current != prerequisite.IntentDigest) reasons.Add("prerequisite-changed");
            }
        }
        var baseline = new BaselineRepository(_database).GetCurrentEvidence(report.Pins.ProjectKey)?.Baseline.Token;
        if (baseline is null) unknown = true;
        else if (!baseline.HasSameSemanticIdentity(report.Pins.BaselineToken)) reasons.Add("baseline-changed");
        if (reasons.Count != 0) return new Freshness(FreshnessRelation.Historical, reasons.Distinct().ToArray());
        return unknown ? new Freshness(FreshnessRelation.Unknown, ["freshness-unavailable"]) :
            new Freshness(FreshnessRelation.Current, []);
    }

    private static CommandOutcome<TrialReviewResult> Invalid(string message) =>
        Refused(RefusalCodes.DifferenceInvalidPage, FailureReason.InvalidArgument, message);
    private static NoTrialResult NoTrial(string proposal) => new(proposal,
        [new ReadTarget(TargetKind.Tool, "Start a Trial", null, "motif_trial", new ReadArguments(Proposal: proposal, Action: "run"))]);
    private static CommandOutcome<TrialReviewResult> Refused(string code, FailureReason reason, string message) =>
        CommandOutcome<TrialReviewResult>.Refused(new Refusal(code, reason, message));
}
