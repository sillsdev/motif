using System.Text.Json;
using SIL.Motif.Contract.Baselines;
using SIL.LCModel;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Retirement;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Projection;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

/// <summary>Queues a Baseline measure and reads its stored Parsimony Report.</summary>
public static class ParsimonyCommands
{
    static ParsimonyCommands() => OperationRegistryBootstrap.Initialize();

    /// <summary>The exact text required before a reviewed negative is staged in a Draft.</summary>
    public const string ReviewedNegativeConfirmationPhrase =
        "I confirm this form or reading is forbidden in the stated context.";

    /// <summary>The exact text required before a reviewed negative can be withdrawn.</summary>
    public const string ReviewedNegativeRetractionConfirmationPhrase =
        "I confirm this reviewed negative should be withdrawn.";

    /// <summary>Reads the current retirement Draft's Dry Run and any stored paired evidence.</summary>
    public static CommandOutcome<RetirementReviewQueryResponse> ReadRetirementReview(
        ReadRetirementReviewRequest request) => RetirementProposalReviewQuery.Read(request);

    /// <summary>Queues the requested fixed measure without capturing or refreshing a Baseline.</summary>
    public static CommandOutcome<JobEnqueuedResponse> Enqueue(EnqueueParsimonyReportRequest request) =>
        Enqueue(request, MeasureRunner.Supports);

    internal static CommandOutcome<JobEnqueuedResponse> Enqueue(
        EnqueueParsimonyReportRequest request, Func<string, bool> supportsMeasure)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(supportsMeasure);
        if (MeasureCatalog.Find(request.MeasureId) is null)
            return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                "parsimony.invalid-measure", FailureReason.InvalidArgument,
                $"Unknown Parsimony measure '{request.MeasureId}'."));
        if (!supportsMeasure(request.MeasureId))
            return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                "parsimony.measure-unavailable", FailureReason.Refused,
                $"Parsimony measure '{request.MeasureId}' is catalogued but not supported by this build."));
        if (!Enum.IsDefined(request.EvidenceScope))
            return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                "parsimony.invalid-scope", FailureReason.InvalidArgument,
                "Evidence scope must be 'default-selection' or 'project-approved'."));
        var assessmentIds = request.AssessmentIds ?? [];
        if (assessmentIds.Any(string.IsNullOrWhiteSpace) ||
            assessmentIds.Distinct(StringComparer.Ordinal).Count() != assessmentIds.Count)
            return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                "parsimony.invalid-assessment", FailureReason.InvalidArgument,
                "Assessment references must be nonblank and unique."));
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var scopeBinding = new ParsimonyScopeBinding(request.EvidenceScope, null);
            var current = new BaselineRepository(database).GetCurrentEvidence(workspaceKey);
            if (assessmentIds.Count > 0)
            {
                if (current is null)
                    return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                        "parsimony.assessment-baseline-mismatch", FailureReason.Refused,
                        "Parser Assessments require the exact current Baseline they measured."));
                var sourceDigest = BatchInvocationEvidence.DigestFile(current.Baseline.FwDataPath);
                var invalid = ValidateAssessmentReferences(database, assessmentIds, current.Baseline.Token,
                    sourceDigest, proposalId: null, proposalIntentDigest: null);
                if (invalid is not null)
                    return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                        "parsimony.invalid-assessment", FailureReason.Refused, invalid));
            }
            var saved = new NamedSelectionRepository(database).GetDefault();
            if (current is not null && saved is not null)
            {
                var resolved = CurrentEvidenceQuery.ResolveSelection(current.Summary, saved);
                if (!resolved.Succeeded)
                    return CommandOutcome<JobEnqueuedResponse>.Refused(resolved.Refusal!);
                var selection = resolved.Value!.Selection;
                scopeBinding = scopeBinding with
                {
                    Selection = new ParsimonySelectionSnapshot(selection.Name, saved.TextIds,
                        saved.AddedWords, selection.Words, selection.Sha256),
                };
            }
            var jobId = CanonicalId.Mint("job/").Value;
            var input = JsonSerializer.Serialize(new ParsimonyJobInput(request.MeasureId, scopeBinding, assessmentIds),
                MotifJson.CreateOptions());
            var job = new JobRepository(database).Create(jobId, workspaceKey,
                SIL.Motif.Worker.Parsimony.ParsimonyJobHandler.JobKind, input,
                DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            return CommandOutcome<JobEnqueuedResponse>.Success(
                new JobEnqueuedResponse(job.JobId, job.Kind, workspaceKey));
        });
    }

    /// <summary>Queues candidate evidence for the exact completed Dry Run named by the request.</summary>
    public static CommandOutcome<JobEnqueuedResponse> EnqueueCandidate(EnqueueParsimonyCandidateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (MeasureCatalog.Find(request.MeasureId) is null)
            return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                "parsimony.invalid-measure", FailureReason.InvalidArgument,
                $"Unknown Parsimony measure '{request.MeasureId}'."));
        if (!MeasureRunner.Supports(request.MeasureId))
            return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                "parsimony.measure-unavailable", FailureReason.Refused,
                $"Parsimony measure '{request.MeasureId}' is catalogued but not supported by this build."));
        if (!Enum.IsDefined(request.EvidenceScope))
            return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                "parsimony.invalid-scope", FailureReason.InvalidArgument,
                "Evidence scope must be 'default-selection' or 'project-approved'."));
        var assessmentIds = request.AssessmentIds ?? [];
        if (assessmentIds.Any(string.IsNullOrWhiteSpace) ||
            assessmentIds.Distinct(StringComparer.Ordinal).Count() != assessmentIds.Count)
            return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                "parsimony.invalid-assessment", FailureReason.InvalidArgument,
                "Assessment references must be nonblank and unique."));

        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var dryRun = new JobRepository(database).Get(request.DryRunJobId);
            if (dryRun is null || dryRun.Kind != JobCommands.DryRunKind ||
                dryRun.Status != JobStatus.CompletedDryRunOnly || !dryRun.DryRunPublished ||
                dryRun.DryRunJson is null)
                return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                    "parsimony.dry-run-required", FailureReason.Refused,
                    "Candidate evidence requires a completed Dry Run job with published effects."));

            DryRunJobInput dryRunInput;
            DryRunSourceBinding sourceBinding;
            try
            {
                dryRunInput = DryRunJobInput.Parse(dryRun.InputJson);
                if (dryRun.ResultJson is null)
                    throw new InvalidDataException("The completed Dry Run has no source binding.");
                var completion = DryRunJobCompletion.Parse(dryRun.ResultJson);
                sourceBinding = completion.SourceBaseline;
                if (dryRunInput.SourceBaseline is not null && dryRunInput.SourceBaseline != sourceBinding)
                    throw new InvalidDataException("The Dry Run input and result name different Baseline sources.");
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IOException)
            {
                return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                    "parsimony.dry-run-evidence-missing", FailureReason.Refused,
                    "This Dry Run lacks frozen source or prerequisite evidence. Rerun its Dry Run."));
            }
            if (assessmentIds.Count > 0)
            {
                var invalid = ValidateAssessmentReferences(database, assessmentIds, sourceBinding.Token,
                    sourceSha256: null, dryRunInput.Proposal.ProposalId,
                    dryRunInput.Proposal.IntentDigest);
                if (invalid is not null)
                    return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                        "parsimony.invalid-assessment", FailureReason.Refused, invalid));
            }
            ParsimonyScopeBinding scopeBinding;
            if (request.EvidenceScope == ParsimonyEvidenceScopeKind.ProjectApproved)
            {
                scopeBinding = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null);
            }
            else
            {
                var current = new BaselineRepository(database).GetCurrentEvidence(dryRun.ProjectKey);
                if (current is null || current.Baseline.Token != sourceBinding.Token)
                    return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                        "parsimony.selection-unavailable", FailureReason.Refused,
                        "The original Baseline's Default Selection is no longer available. Use 'project-approved' " +
                        "scope or rerun the Dry Run against the current Baseline."));
                var saved = new NamedSelectionRepository(database).GetDefault();
                if (saved is null)
                    return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                        "parsimony.selection-unavailable", FailureReason.Refused,
                        "No Default Selection is saved for this project."));
                var resolved = CurrentEvidenceQuery.ResolveSelection(current.Summary, saved);
                if (!resolved.Succeeded) return CommandOutcome<JobEnqueuedResponse>.Refused(resolved.Refusal!);
                var selection = resolved.Value!.Selection;
                scopeBinding = new ParsimonyScopeBinding(request.EvidenceScope,
                    new ParsimonySelectionSnapshot(selection.Name, saved.TextIds, saved.AddedWords,
                        selection.Words, selection.Sha256));
            }

            RetirementExpectationTranslation? retirementTranslation;
            try
            {
                retirementTranslation = CaptureRetirementTranslation(database, dryRunInput, sourceBinding,
                    scopeBinding, dryRun.DryRunJson);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                               InvalidDataException or JsonException or FormatException or
                                               IOException or NotSupportedException)
            {
                return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                    "parsimony.retirement-expectations-unavailable", FailureReason.Refused,
                    "Frozen retirement expectations could not be captured from the Dry Run source: " +
                    exception.Message));
            }

            var input = new ParsimonyCandidateJobInput(ParsimonyCandidateJobInput.CurrentSchemaVersion,
                dryRun.JobId, request.MeasureId, scopeBinding, assessmentIds, retirementTranslation);
            var jobId = CanonicalId.Mint("job/").Value;
            var json = JsonSerializer.Serialize(input, MotifJson.CreateOptions());
            var created = new JobRepository(database).Create(jobId, dryRun.ProjectKey,
                ParsimonyCandidateEvidenceBuilder.JobKind, json,
                DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            return CommandOutcome<JobEnqueuedResponse>.Success(
                new JobEnqueuedResponse(created.JobId, created.Kind, created.ProjectKey));
        });
    }

    private static RetirementExpectationTranslation? CaptureRetirementTranslation(MotifDatabase database,
        DryRunJobInput dryRunInput, DryRunSourceBinding sourceBinding, ParsimonyScopeBinding scopeBinding,
        string? publishedDryRunJson)
    {
        var proposalId = CanonicalId.Parse(dryRunInput.Proposal.ProposalId);
        var draftRecord = new ProposalRepository(database).ListDrafts()
            .SingleOrDefault(item => item.ProposalId == proposalId);
        if (draftRecord?.ProposalJson is not { } draftJson) return null;
        var draft = JsonSerializer.Deserialize<DraftDocument>(draftJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The retirement Draft is empty.");
        var proposal = dryRunInput.Proposal.Validate();
        var retirement = RetirementReviewEvidenceCapture.FindRetirementComposition(draft, proposal);
        if (retirement is null) return null;
        if (publishedDryRunJson is null)
            throw new InvalidDataException("The completed retirement Dry Run has no published effects.");

        using var source = BaselineReadCache.Open(sourceBinding.FwDataPath);
        return RetirementReviewEvidenceCapture.CaptureTranslation(draftJson, proposal, sourceBinding.Token,
            source.Cache, JobCommands.ParsePublishedDryRun(publishedDryRunJson), scopeBinding);
    }

    private static string? ValidateAssessmentReferences(MotifDatabase database,
        IReadOnlyList<string> assessmentIds, BaselineToken baselineToken, string? sourceSha256,
        string? proposalId, string? proposalIntentDigest)
    {
        var repository = new AssessmentRepository(database);
        foreach (var assessmentId in assessmentIds)
        {
            AssessmentRecord assessment;
            try
            {
                assessment = repository.Get(assessmentId);
            }
            catch (KeyNotFoundException)
            {
                return $"Parser Assessment '{assessmentId}' was not found.";
            }
            BaselineToken? recordedToken;
            try
            {
                recordedToken = JsonSerializer.Deserialize<BaselineToken>(assessment.BaselineToken,
                    MotifJson.CreateOptions());
            }
            catch (JsonException)
            {
                return $"Parser Assessment '{assessmentId}' has an invalid Baseline binding.";
            }
            var invocation = assessment.Invocation;
            if (recordedToken != baselineToken || assessment.Assessor != "pangloss" ||
                assessment.Kind != AssessmentKinds.ParseTime || assessment.ProposalId?.Value != proposalId ||
                assessment.ProposalIntentDigest != proposalIntentDigest || invocation is null ||
                assessment.GrammarSourceSha256 != invocation.SourceBytesSha256 || invocation.Threads < 1)
                return $"Parser Assessment '{assessmentId}' does not match the exact Baseline or Proposal.";
            if (sourceSha256 is not null && invocation.SourceBytesSha256 != sourceSha256)
                return $"Parser Assessment '{assessmentId}' was measured against different .fwdata bytes.";
        }
        return null;
    }

    /// <summary>Waits for candidate evidence and returns the stored before and after Reports.</summary>
    public static CommandOutcome<ParsimonyCandidateEvidenceResponse> WaitCandidate(
        WaitForParsimonyCandidateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var waited = JobWait.WaitAsync(request.FwDataPath, request.JobId, null, CancellationToken.None,
            request.Timeout, request.ProductVersion).GetAwaiter().GetResult();
        if (!waited.Succeeded) return CommandOutcome<ParsimonyCandidateEvidenceResponse>.Refused(waited.Refusal!);
        if (waited.Value!.Status != JobStatus.Completed)
        {
            var detail = ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
                CommandOutcome<string>.Success(ReadJobFailure(database, request.JobId)));
            return CommandOutcome<ParsimonyCandidateEvidenceResponse>.Refused(new Refusal(
                "parsimony.candidate-failed", FailureReason.Refused,
                detail.Succeeded ? detail.Value! : "The candidate Parsimony job did not complete."));
        }

        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var resultJson = new JobRepository(database).Get(request.JobId)?.ResultJson;
            if (resultJson is null)
                return CommandOutcome<ParsimonyCandidateEvidenceResponse>.Refused(new Refusal(
                    "parsimony.candidate-result-missing", FailureReason.StoreInconsistent,
                    "The completed candidate job has no stored evidence response."));
            try
            {
                var response = JsonSerializer.Deserialize<ParsimonyCandidateEvidenceResponse>(resultJson,
                    MotifJson.CreateOptions());
                if (response is null || response.SchemaVersion != 1 || response.Candidate.DryRunJobId.Length == 0)
                    throw new JsonException("The candidate response shape is incomplete.");
                return CommandOutcome<ParsimonyCandidateEvidenceResponse>.Success(response);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
            {
                return CommandOutcome<ParsimonyCandidateEvidenceResponse>.Refused(new Refusal(
                    "parsimony.candidate-result-invalid", FailureReason.StoreInconsistent,
                    "The stored candidate evidence response is malformed."));
            }
        });
    }

    /// <summary>Waits for one report job and returns the report recorded by that job.</summary>
    public static CommandOutcome<ParsimonyReportResponse> Wait(WaitForParsimonyReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var waited = JobWait.WaitAsync(request.FwDataPath, request.JobId, null, CancellationToken.None,
            request.Timeout, request.ProductVersion).GetAwaiter().GetResult();
        if (!waited.Succeeded) return CommandOutcome<ParsimonyReportResponse>.Refused(waited.Refusal!);
        if (waited.Value!.Status != JobStatus.Completed)
        {
            var detail = ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
                CommandOutcome<string>.Success(ReadJobFailure(database, request.JobId)));
            var message = detail.Succeeded ? detail.Value! :
                $"Parsimony job '{request.JobId}' finished as {JobStatusJson.ToWire(waited.Value.Status!.Value)}.";
            return CommandOutcome<ParsimonyReportResponse>.Refused(new Refusal(
                "parsimony.job-failed", FailureReason.Refused, message));
        }

        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var job = new JobRepository(database).Get(request.JobId);
            if (job?.ResultJson is null)
                return CommandOutcome<ParsimonyReportResponse>.Refused(new Refusal(
                    "parsimony.result-missing", FailureReason.StoreInconsistent,
                    "The completed Parsimony job has no stored Report identity."));
            string reportId;
            try
            {
                using var result = JsonDocument.Parse(job.ResultJson);
                reportId = result.RootElement.GetProperty("reportId").GetString() ?? string.Empty;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                return CommandOutcome<ParsimonyReportResponse>.Refused(new Refusal(
                    "parsimony.result-invalid", FailureReason.StoreInconsistent,
                    "The completed Parsimony job has a malformed Report identity."));
            }
            return ReadReport(database, reportId);
        });
    }

    /// <summary>Reads a previously stored Parsimony Report without invoking PanGloss.</summary>
    public static CommandOutcome<ParsimonyReportResponse> Show(ShowParsimonyReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ReportId))
            return CommandOutcome<ParsimonyReportResponse>.Refused(new Refusal(
                "parsimony.report-id-required", FailureReason.InvalidArgument,
                "A Report id is required."));
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion,
            (database, _) => ReadReport(database, request.ReportId));
    }

    /// <summary>Names the newest stored Parsimony Report and its bundle, reading the store only and never a parser.</summary>
    public static CommandOutcome<ParsimonyLatestReportResponse> ReadLatest(ReadLatestParsimonyReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FwDataPath) || string.IsNullOrWhiteSpace(request.ProductVersion))
            return Refused<ParsimonyLatestReportResponse>("parsimony.invalid-latest-request", FailureReason.InvalidArgument,
                "A FieldWorks project path and a Motif product version are required.");
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var latest = new ReportRepository(database).GetLatest("parsimony");
            if (latest is null)
                return Refused<ParsimonyLatestReportResponse>("parsimony.no-report", FailureReason.NotFound,
                    "No Parsimony Report yet.");
            var stored = ReadReport(database, latest.ReportId);
            if (!stored.Succeeded) return CommandOutcome<ParsimonyLatestReportResponse>.Refused(stored.Refusal!);
            return CommandOutcome<ParsimonyLatestReportResponse>.Success(
                new ParsimonyLatestReportResponse(stored.Value!.ReportId, stored.Value.Inputs.BundleId));
        });
    }

    internal const string ReportBaselineMismatch =
        "The Report was measured from a different Baseline. Run the measure again against the current Baseline.";

    /// <summary>Composes a disposition from one exact finding in the current project's stored Report.</summary>
    public static CommandOutcome<ComposedOperationsResponse> RecordDisposition(
        RecordParsimonyDispositionFromFindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ReportId) || string.IsNullOrWhiteSpace(request.FindingId) ||
            string.IsNullOrWhiteSpace(request.Disposition) || string.IsNullOrWhiteSpace(request.RecordTypeId) ||
            string.IsNullOrWhiteSpace(request.DraftName))
            return Refused<ComposedOperationsResponse>("parsimony.disposition-request-invalid",
                FailureReason.InvalidArgument, "A Report, finding, disposition, Notebook record type, and Draft are required.");
        if (!CanonicalId.TryParse(request.RecordTypeId, out _))
            return Refused<ComposedOperationsResponse>("parsimony.record-type-id-invalid",
                FailureReason.InvalidArgument, "--record-type must be the portable ID shown by parsimony record-types.");
        if (string.IsNullOrWhiteSpace(request.ProductVersion))
            return Refused<ComposedOperationsResponse>("parsimony.disposition-request-invalid",
                FailureReason.InvalidArgument, "A Motif product version is required.");

        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var stored = ReadReport(database, request.ReportId);
            if (!stored.Succeeded) return CommandOutcome<ComposedOperationsResponse>.Refused(stored.Refusal!);

            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var baselines = new BaselineRepository(database);
            var current = baselines.GetCurrent(workspaceKey);
            var evidenceIsCurrent = current is not null && current.Token == stored.Value!.Inputs.BaselineToken;
            // A Report measured just before an Apply may still confirm its decision was recorded; it stages nothing.
            var measuredBeforeApply = !evidenceIsCurrent &&
                baselines.GetLatestCaptured(workspaceKey)?.Token == stored.Value!.Inputs.BaselineToken;
            if (!evidenceIsCurrent && !measuredBeforeApply)
                return Refused<ComposedOperationsResponse>("parsimony.report-baseline-mismatch",
                    FailureReason.Refused, ReportBaselineMismatch);

            var matches = stored.Value.Findings.Where(item =>
                string.Equals(item.FindingId, request.FindingId, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0)
                return Refused<ComposedOperationsResponse>("parsimony.finding-not-in-report",
                    FailureReason.NotFound,
                    $"Finding '{request.FindingId}' is not in Parsimony Report '{request.ReportId}'.");
            if (matches.Length > 1)
                return Refused<ComposedOperationsResponse>("parsimony.finding-ambiguous",
                    FailureReason.Refused,
                    $"Finding '{request.FindingId}' matches {matches.Length} findings in Parsimony Report " +
                    $"'{request.ReportId}', so no single finding can be disposed.");
            var finding = matches[0];

            return ProposalCommands.ComposeRecordParsimonyDispositionFromFinding(
                database, project, request, stored.Value, finding, evidenceIsCurrent);
        });
    }

    /// <summary>Stages an agent-attributed revision of a saved disposition in a Draft Proposal.</summary>
    public static CommandOutcome<ComposedOperationsResponse> ReviseDisposition(
        ReviseParsimonyDispositionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FwDataPath) || string.IsNullOrWhiteSpace(request.ProductVersion) ||
            string.IsNullOrWhiteSpace(request.DraftName) || string.IsNullOrWhiteSpace(request.IntentJson))
            return Refused<ComposedOperationsResponse>("parsimony.disposition-revision-request-invalid",
                FailureReason.InvalidArgument, "A project, product version, Draft, and revision intent are required.");
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
            ProposalCommands.ComposeReviseParsimonyDisposition(database, project, request.DraftName,
                request.IntentJson));
    }

    /// <summary>Stages an agent-attributed retraction of a saved disposition in a Draft Proposal.</summary>
    public static CommandOutcome<ComposedOperationsResponse> RetractDisposition(
        RetractParsimonyDispositionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FwDataPath) || string.IsNullOrWhiteSpace(request.ProductVersion) ||
            string.IsNullOrWhiteSpace(request.DraftName) || string.IsNullOrWhiteSpace(request.IntentJson))
            return Refused<ComposedOperationsResponse>("parsimony.disposition-retraction-request-invalid",
                FailureReason.InvalidArgument, "A project, product version, Draft, and retraction intent are required.");
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
            ProposalCommands.ComposeRetractParsimonyDisposition(database, project, request.DraftName,
                request.IntentJson));
    }

    /// <summary>Lists saved Notebook record types with their portable IDs for explicit selection.</summary>
    public static CommandOutcome<NotebookRecordTypesResponse> ListNotebookRecordTypes(
        ListNotebookRecordTypesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FwDataPath))
            return Refused<NotebookRecordTypesResponse>("project.invalid", FailureReason.InvalidArgument,
                "A FieldWorks project path is required.");
        try
        {
            var project = ProjectStoreCommand.Locate(request.FwDataPath);
            var types = ProjectReadCache.ReadSaved(project, (_, cache) =>
            {
                var root = cache.LangProject.ResearchNotebookOA?.RecTypesOA;
                if (root is null) return Array.Empty<NotebookRecordType>();
                var result = new List<NotebookRecordType>();
                void AddChildren(IEnumerable<ICmPossibility> possibilities)
                {
                    foreach (var possibility in possibilities)
                    {
                        var name = WritingSystemTextReader.BestAnalysis(cache, possibility.Name).Text;
                        result.Add(new NotebookRecordType(CanonicalId.FromGuid(possibility.Guid).Value,
                            string.IsNullOrWhiteSpace(name) ? "(unnamed record type)" : name));
                        AddChildren(possibility.SubPossibilitiesOS.Cast<ICmPossibility>());
                    }
                }
                AddChildren(root.PossibilitiesOS.Cast<ICmPossibility>());
                return result.OrderBy(item => item.Name, StringComparer.Ordinal)
                    .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
            });
            return CommandOutcome<NotebookRecordTypesResponse>.Success(new NotebookRecordTypesResponse(types));
        }
        catch (FileNotFoundException exception)
        {
            return Refused<NotebookRecordTypesResponse>("project.not-found", FailureReason.NotFound, exception.Message);
        }
        catch (ProjectSavingException)
        {
            return Refused<NotebookRecordTypesResponse>("project.saving", FailureReason.Busy,
                "FieldWorks is saving the project. Try again in a moment.");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or
                                          InvalidDataException or LcmInitializationException)
        {
            return Refused<NotebookRecordTypesResponse>("project.unreadable", FailureReason.Refused,
                "Motif could not read the saved Notebook record types: " + exception.Message);
        }
    }

    /// <summary>Reads the saved, exact positive and negative expectation input from the project.</summary>
    public static CommandOutcome<ParsimonyExpectationProjection> ReadExpectations(
        ReadParsimonyExpectationsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FwDataPath))
            return Refused<ParsimonyExpectationProjection>("project.invalid", FailureReason.InvalidArgument,
                "A FieldWorks project path is required.");
        try
        {
            var project = ProjectStoreCommand.Locate(request.FwDataPath);
            var projection = ProjectReadCache.ReadSaved(project, (_, cache) =>
                ParsimonyExpectationProjectionBuilder.Build(cache));
            return CommandOutcome<ParsimonyExpectationProjection>.Success(projection);
        }
        catch (FileNotFoundException exception)
        {
            return Refused<ParsimonyExpectationProjection>("project.not-found", FailureReason.NotFound,
                exception.Message);
        }
        catch (ProjectSavingException)
        {
            return Refused<ParsimonyExpectationProjection>("project.saving", FailureReason.Busy,
                "FieldWorks is saving the project. Try again in a moment.");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or
                                          InvalidDataException or LcmInitializationException)
        {
            return Refused<ParsimonyExpectationProjection>("project.unreadable", FailureReason.Refused,
                "Motif could not read the saved Parsimony expectations: " + exception.Message);
        }
    }

    /// <summary>Stages a human-confirmed surface or reading negative in the named Draft.</summary>
    public static CommandOutcome<ComposedOperationsResponse> ConfirmReviewedNegative(
        ConfirmReviewedNegativeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.Confirmation, ReviewedNegativeConfirmationPhrase, StringComparison.Ordinal))
            return Refused<ComposedOperationsResponse>("parsimony.negative-confirmation-required",
                FailureReason.InvalidArgument,
                "Confirm this reviewed negative with: " + ReviewedNegativeConfirmationPhrase);
        if (string.IsNullOrWhiteSpace(request.DraftName) || string.IsNullOrWhiteSpace(request.IntentJson) ||
            string.IsNullOrWhiteSpace(request.ProductVersion))
            return Refused<ComposedOperationsResponse>("parsimony.negative-request-invalid",
                FailureReason.InvalidArgument, "A Draft, reviewed-negative intent, and Motif product version are required.");
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
            ProposalCommands.ComposeRecordReviewedNegative(database, project, request.DraftName, request.IntentJson));
    }

    /// <summary>Stages a human-confirmed withdrawal of an exact current reviewed-negative revision head.</summary>
    public static CommandOutcome<ComposedOperationsResponse> RetractReviewedNegative(
        RetractReviewedNegativeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.Confirmation, ReviewedNegativeRetractionConfirmationPhrase, StringComparison.Ordinal))
            return Refused<ComposedOperationsResponse>("parsimony.negative-confirmation-required",
                FailureReason.InvalidArgument,
                "Confirm this reviewed negative retraction with: " + ReviewedNegativeRetractionConfirmationPhrase);
        if (string.IsNullOrWhiteSpace(request.DraftName) || string.IsNullOrWhiteSpace(request.IntentJson) ||
            string.IsNullOrWhiteSpace(request.ProductVersion))
            return Refused<ComposedOperationsResponse>("parsimony.negative-request-invalid",
                FailureReason.InvalidArgument, "A Draft, retraction intent, and Motif product version are required.");
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
            ProposalCommands.ComposeRetractReviewedNegative(database, project, request.DraftName, request.IntentJson));
    }

    private static CommandOutcome<ParsimonyReportResponse> ReadReport(MotifDatabase database, string reportId)
    {
        var report = new ReportRepository(database).Get(reportId);
        if (report is null || report.Kind != "parsimony")
            return CommandOutcome<ParsimonyReportResponse>.Refused(new Refusal(
                "parsimony.report-not-found", FailureReason.NotFound,
                $"Parsimony Report '{reportId}' was not found."));
        try
        {
            var stored = JsonSerializer.Deserialize<ParsimonyReportResponse>(report.ReportJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (stored is null || stored.ReportId != report.ReportId || !IsComplete(stored) ||
                stored.JoinQuality is null ||
                !stored.AssessmentIds.SequenceEqual(stored.Inputs.AssessmentIds, StringComparer.Ordinal))
                throw new JsonException("The stored Parsimony Report shape is inconsistent.");
            return CommandOutcome<ParsimonyReportResponse>.Success(stored);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return CommandOutcome<ParsimonyReportResponse>.Refused(new Refusal(
                "parsimony.report-damaged", FailureReason.StoreInconsistent,
                $"Parsimony Report '{reportId}' is damaged: {exception.Message} {DamagedReportGuidance}"));
        }
    }

    /// <summary>
    /// What to do about a stored Report this version cannot read. A Report from an earlier version is refused
    /// rather than read through a compatibility path, and a fresh run of its measure writes a new one.
    /// </summary>
    internal const string DamagedReportGuidance = "Run the measure again for a new Report; this one cannot be read.";

    /// <summary>
    /// Whether a deserialized Report has every required member. JSON can hold an explicit null where the
    /// record expects a value, and such a Report is refused as damaged rather than read.
    /// </summary>
    internal static bool IsComplete(ParsimonyReportResponse report) =>
        report.ReportId is not null && report.Text is not null && report.Inputs is not null &&
        report.AssessmentIds is not null && report.Findings is not null && report.Notes is not null &&
        report.Notes.All(note => note is not null && note.Kind is not null && note.MeasureId is not null &&
            note.Text is not null) &&
        report.Findings.All(finding =>
            finding.FindingId is not null && finding.MeasureId is not null &&
            finding.AttachesTo is { Identity: not null } &&
            finding.Number is not null && finding.Threshold is not null && finding.EvidenceDigest is not null &&
            finding.EvidenceRefs is { } refs && refs.All(reference => reference is not null) &&
            finding.RecipeLink is not null && finding.Limitations is not null);

    private static string ReadJobFailure(MotifDatabase database, string jobId)
    {
        var resultJson = new JobRepository(database).Get(jobId)?.ResultJson;
        if (resultJson is null) return $"Parsimony job '{jobId}' did not complete.";
        try
        {
            using var result = JsonDocument.Parse(resultJson);
            if (result.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                return detail.GetString()!;
        }
        catch (JsonException) { }
        return $"Parsimony job '{jobId}' did not complete.";
    }

    private static CommandOutcome<T> Refused<T>(string code, FailureReason reason, string message)
        where T : class => CommandOutcome<T>.Refused(new Refusal(code, reason, message));
}
