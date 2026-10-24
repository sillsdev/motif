using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads a complete retained all-measure report set matching the current Baseline, parser, and scope.</summary>
public static class ParsimonyReportSetQuery
{
    /// <summary>Returns the newest reusable set, a typed refusal, or no set when current evidence needs new reports.</summary>
    /// <param name="request">The recorded project and product version whose retained reports are read.</param>
    /// <param name="parserPath">The parser whose current byte identity must match the completed job.</param>
    /// <param name="cancellationToken">Stops this read without starting a new report job.</param>
    public static async Task<CommandOutcome<ParsimonyReportSetResponse?>> QueryAsync(
        ReadLatestParsimonyReportRequest request, string? parserPath, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return WaitCancelled<ParsimonyReportSetResponse?>();
        var candidates = await Task.Run(() => ReadParsimonyCandidates(request, parserPath)).ConfigureAwait(false);
        if (!candidates.Succeeded) return CommandOutcome<ParsimonyReportSetResponse?>.Refused(candidates.Refusal!);
        foreach (var candidate in candidates.Value!.Jobs)
        {
            if (cancellationToken.IsCancellationRequested) return WaitCancelled<ParsimonyReportSetResponse?>();
            var read = await ParsimonyCommands.WaitAllAsync(new WaitForParsimonyReportSetRequest(
                request.FwDataPath, request.ProductVersion, candidate.JobId, TimeSpan.Zero), cancellationToken)
                .ConfigureAwait(false);
            if (!read.Succeeded)
            {
                if (read.Refusal!.Code is ParsimonyRefusalCodes.ReportNotFound or ParsimonyRefusalCodes.ReportDamaged or
                    ParsimonyRefusalCodes.ResultMissing or ParsimonyRefusalCodes.ResultInvalid) continue;
                return CommandOutcome<ParsimonyReportSetResponse?>.Refused(read.Refusal);
            }
            var set = read.Value!;
            if (!MatchesReports(set, candidates.Value.Token, candidates.Value.SelectionSha256)) continue;
            var descriptor = await Task.Run(() => ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion,
                (database, project) =>
                {
                    var bundle = new EvidenceArtifactRepository(database).Get(ProjectWorkspaceKey.Compute(project), set.BundleId);
                    return CommandOutcome<ParsimonyBundleRecord?>.Success(bundle);
                })).ConfigureAwait(false);
            if (!descriptor.Succeeded) return CommandOutcome<ParsimonyReportSetResponse?>.Refused(descriptor.Refusal!);
            if (descriptor.Value is not { State: "available" } bundle || !MatchesBundle(bundle, set.Reports[0].Inputs))
                continue;
            var usable = true;
            foreach (var report in set.Reports)
            {
                if (cancellationToken.IsCancellationRequested) return WaitCancelled<ParsimonyReportSetResponse?>();
                var view = await Task.Run(() => ParsimonyViewsCommand.View(new ReadParsimonyViewRequest(
                    request.FwDataPath, request.ProductVersion, new ParsimonyNamedViewRequest(set.BundleId,
                        ParsimonyViews.ParsimonyActiveFindings, new ParsimonyViewFilters(ReportId: report.ReportId), 1))))
                    .ConfigureAwait(false);
                if (view.Succeeded) continue;
                if (view.Refusal!.Code is ParsimonyRefusalCodes.BundleNotFound or ParsimonyRefusalCodes.BundleInvalid or
                    ParsimonyRefusalCodes.ReportDamaged or ParsimonyRefusalCodes.ReportNotFound or
                    ParsimonyRefusalCodes.ReportBundleMismatch or ParsimonyRefusalCodes.ReportBaselineMismatch)
                {
                    usable = false;
                    break;
                }
                return CommandOutcome<ParsimonyReportSetResponse?>.Refused(view.Refusal);
            }
            if (!usable) continue;
            var current = await Task.Run(() => ReadParsimonyCandidates(request, parserPath)).ConfigureAwait(false);
            if (!current.Succeeded) return CommandOutcome<ParsimonyReportSetResponse?>.Refused(current.Refusal!);
            if (cancellationToken.IsCancellationRequested) return WaitCancelled<ParsimonyReportSetResponse?>();
            if (current.Value!.Token == candidates.Value.Token &&
                current.Value.Jobs.Any(job => job.JobId == candidate.JobId))
                return CommandOutcome<ParsimonyReportSetResponse?>.Success(set);
        }
        return CommandOutcome<ParsimonyReportSetResponse?>.Success(null);
    }

    private sealed record ParsimonyCandidates(BaselineToken? Token, string? SelectionSha256, IReadOnlyList<JobRecord> Jobs);

    private static CommandOutcome<ParsimonyCandidates> ReadParsimonyCandidates(
        ReadLatestParsimonyReportRequest request, string? parserPath) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var key = ProjectWorkspaceKey.Compute(project);
            var current = new BaselineRepository(database).GetCurrentEvidence(key);
            var parserKey = ParserIdentity.KeyOf(parserPath);
            if (current is null || parserKey is null)
                return CommandOutcome<ParsimonyCandidates>.Success(new ParsimonyCandidates(current?.Baseline.Token, null, []));
            ParsimonySelectionSnapshot? selection = null;
            var saved = new NamedSelectionRepository(database).GetDefault();
            if (saved is not null)
            {
                var resolved = CurrentEvidenceQuery.ResolveSelection(current.Summary, saved);
                if (!resolved.Succeeded) return CommandOutcome<ParsimonyCandidates>.Refused(resolved.Refusal!);
                var value = resolved.Value!.Selection;
                selection = new ParsimonySelectionSnapshot(value.Name, saved.TextIds, saved.AddedWords, value.Words, value.Sha256);
            }
            var scope = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, selection);
            var jobs = new JobRepository(database).ListByProjectAndKind(key, ParsimonyJobHandler.JobKind)
                .Where(job => job.Status == JobStatus.Completed && JobParserPin.KeyOf(job.InputJson) == parserKey &&
                    MatchesScope(job.InputJson, scope))
                .OrderByDescending(job => job.UpdatedUtc, StringComparer.Ordinal)
                .ThenByDescending(job => job.JobId, StringComparer.Ordinal).ToArray();
            return CommandOutcome<ParsimonyCandidates>.Success(new ParsimonyCandidates(current.Baseline.Token, selection?.SelectionSha256.Replace("sha256:", "", StringComparison.Ordinal), jobs));
        });

    private static bool MatchesScope(string inputJson, ParsimonyScopeBinding expected)
    {
        try
        {
            var input = JsonSerializer.Deserialize<ParsimonyJobInput>(inputJson, MotifJson.CreateOptions());
            return input is { MeasureId: ParsimonyAdmission.AllMeasures, ScopeBinding: not null } &&
                (input.AssessmentIds?.Count ?? 0) == 0 &&
                JsonSerializer.Serialize(input.ScopeBinding, MotifJson.CreateOptions()) ==
                JsonSerializer.Serialize(expected, MotifJson.CreateOptions());
        }
        catch (JsonException) { return false; }
    }

    private static bool MatchesReports(ParsimonyReportSetResponse set, BaselineToken? token, string? selectionSha256)
    {
        var measures = ParsimonyAdmission.SupportedMeasureIds();
        if (token is null || set.Reports.Count != measures.Count ||
            set.Reports.Any(report => report.MeasureRuns is null || report.MeasureRuns.Any(run => run is null ||
                !Enum.IsDefined(run.Status))) ||
            set.Reports.Select(report => report.ReportId).Distinct(StringComparer.Ordinal).Count() != measures.Count)
            return false;
        var runs = set.Reports.SelectMany(report => report.MeasureRuns).Select(run => run.MeasureId).ToArray();
        return runs.Order(StringComparer.Ordinal).SequenceEqual(measures.Order(StringComparer.Ordinal)) &&
            set.Reports.All(report => report.MeasureRuns.Count == 1 && report.Inputs.BaselineToken == token &&
                report.Inputs.BundleId == set.BundleId && report.Inputs.InputKind == "baseline" &&
                report.Inputs.Candidate is null && report.Inputs.EvidenceScope == ParsimonyEvidenceScopeKind.ProjectApproved &&
                report.Inputs.AssessmentIds.Count == 0 && report.AssessmentIds.Count == 0 &&
                report.Inputs.SelectionSha256 == selectionSha256 &&
                JsonSerializer.Serialize(report.Inputs) == JsonSerializer.Serialize(set.Reports[0].Inputs) &&
                report.Findings.All(finding =>
                    finding.MeasureId == report.MeasureRuns[0].MeasureId &&
                    finding.Threshold == MeasureCatalog.Find(finding.MeasureId)?.Threshold &&
                    finding.Axis == MeasureCatalog.Find(finding.MeasureId)?.Axis &&
                    finding.Tier == MeasureCatalog.Find(finding.MeasureId)?.Tier));
    }

    private static bool MatchesBundle(ParsimonyBundleRecord bundle, ParsimonyReportInputs inputs) =>
        bundle.InputKind == "baseline" && bundle.CandidateIdentity is null &&
        bundle.GrammarFactsSchemaVersion == PanGlossInterfaceVersions.FactsSchemaVersion &&
        bundle.EvidenceSchemaVersion == EvidenceSchema.Version &&
        inputs.GrammarFacts == new ParsimonyArtifactDigest(bundle.GrammarFactsSchemaVersion, bundle.GrammarFactsSha256) &&
        inputs.Evidence == new ParsimonyArtifactDigest(bundle.EvidenceSchemaVersion, bundle.EvidenceSha256) &&
        inputs.ModelFingerprint == bundle.ModelFingerprint;

    private static CommandOutcome<T> WaitCancelled<T>() where T : class? =>
        CommandOutcome<T>.Refused(new Refusal(
            RefusalCodes.ProjectWaitCancelled, FailureReason.Cancelled, "Waiting to use the project was cancelled."));
}
