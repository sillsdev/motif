using System.Text;
using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Store;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Effects;
using SIL.Motif.Projection;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using DryRunModel = SIL.Motif.Model.DryRun.DryRun;

namespace SIL.Motif.Commands;

/// <summary>
/// The verbs that put durable work in the queue and read back what became of it.
/// </summary>
/// <remarks>
/// Both open the paired database directly, like every other verb. Enqueueing returns as soon as the row
/// is committed rather than waiting for the work, because the runner that will do it is a different
/// process and may not even be started yet — the row is the whole handoff.
/// </remarks>
public static class JobCommands
{
    /// <summary>The durable kind a queued Baseline refresh carries.</summary>
    public const string BaselineRefreshKind = "baseline-refresh";

    /// <summary>The durable kind a queued Dry Run carries.</summary>
    public const string DryRunKind = "dry-run";

    /// <summary>The durable kind a queued Trial carries.</summary>
    public const string TrialKind = "trial";

    /// <summary><c>--wait</c>'s default bound before it gives up and reports the job still unfinished.</summary>
    public static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Queues a Baseline refresh for one project and returns the job id that names it.</summary>
    public static CommandOutcome<JobEnqueuedResponse> EnqueueBaselineRefresh(EnqueueBaselineRefreshRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var jobs = new JobRepository(database);
            var jobId = CanonicalId.Mint("job/").Value;
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var created = jobs.Create(jobId, workspaceKey, BaselineRefreshKind,
                "{}", NowStamp());
            return CommandOutcome<JobEnqueuedResponse>.Success(
                new JobEnqueuedResponse(created.JobId, BaselineRefreshKind, workspaceKey));
        });
    }

    /// <summary>
    /// Freezes a Draft or finalized Proposal and its prerequisite closure before queueing a Dry Run.
    /// </summary>
    public static CommandOutcome<JobEnqueuedResponse> EnqueueDryRun(EnqueueDryRunRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var repository = new ProposalRepository(database);
            ProposalRecord record;
            try
            {
                var id = ProposalCommands.NormalizeId(request.ProposalId);
                record = repository.Get(CanonicalId.Parse(id));
                if (record.DraftName is null) (record, _) = repository.GetFinalized(CanonicalId.Parse(id));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<JobEnqueuedResponse>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
            }

            var proposalJson = CanonicalProposalJson(record);
            var proposal = ProposalJsonParser.Parse(proposalJson);
            IReadOnlyList<FrozenProposalRevision> prerequisites;
            try
            {
                prerequisites = repository.FreezePrerequisites(proposal);
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<JobEnqueuedResponse>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
            }

            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var baseline = new BaselineRepository(database).GetCurrentPublished(workspaceKey);
            DryRunSourceBinding? source = null;
            try
            {
                if (baseline is not null)
                    source = new DryRunSourceBinding(baseline.Token, baseline.RootDirectory, baseline.FwDataPath,
                        SourceDigest(baseline.FwDataPath));
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                // The copy vanished after the check; the job then waits for a fresh capture.
            }
            var input = new DryRunJobInput(DryRunJobInput.CurrentSchemaVersion,
                FrozenProposalRevision.Create(proposalJson), prerequisites, source);
            var jobs = new JobRepository(database);
            var jobId = CanonicalId.Mint("job/").Value;
            var inputJson = JsonSerializer.Serialize(input, MotifJson.CreateOptions());
            var created = jobs.Create(jobId, workspaceKey, DryRunKind, inputJson, NowStamp());
            return CommandOutcome<JobEnqueuedResponse>.Success(
                new JobEnqueuedResponse(created.JobId, DryRunKind, workspaceKey));
        });

    /// <summary>
    /// Loads one Proposal — a committed revision or an uncommitted Draft, either resolves — through
    /// <see cref="ProposalRepository.Get"/>, refusing before any row is queued when it is absent, then
    /// queues a Trial job and returns the job id that names it. A request with neither words nor all words
    /// measures <see cref="DerivedTrialWords"/>, so every front end tries a Proposal the same way.
    /// </summary>
    public static CommandOutcome<JobEnqueuedResponse> EnqueueTrial(EnqueueTrialRequest request,
        string? parserPath = null)
    {
        if (request.Words?.Any(word => string.IsNullOrWhiteSpace(word) ||
                !string.Equals(word, word.Trim(), StringComparison.Ordinal)) == true)
            return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                "job.invalid-words", FailureReason.InvalidArgument,
                "Trial words must be nonblank and have no surrounding whitespace."));
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var repository = new ProposalRepository(database);
            ProposalRecord record;
            try
            {
                var id = ProposalCommands.NormalizeId(request.ProposalId);
                record = repository.Get(CanonicalId.Parse(id));
                if (record.DraftName is null) (record, _) = repository.GetFinalized(CanonicalId.Parse(id));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<JobEnqueuedResponse>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
            }

            if (record.DraftName is not null && request.ExpectedDraftRevision is { } expectedRevision &&
                DraftRevision.Compute(record.ProposalJson) != expectedRevision)
                return CommandOutcome<JobEnqueuedResponse>.Refused(new Refusal(
                    "draft.revision-conflict", FailureReason.Refused,
                    "The Draft changed after it was checked. Reload it before queueing a Trial.",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["draftName"] = record.DraftName }));

            var jobs = new JobRepository(database);
            var jobId = CanonicalId.Mint("job/").Value;
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var proposalJson = CanonicalProposalJson(record);
            var proposal = ProposalJsonParser.Parse(proposalJson);
            IReadOnlyList<FrozenProposalRevision> prerequisites;
            try
            {
                prerequisites = repository.FreezePrerequisites(proposal);
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<JobEnqueuedResponse>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
            }
            var words = request.Words ?? (request.AllWords ? null : DerivedTrialWords(proposal));
            var input = new TrialJobInput(proposalJson, request.Scope, words, request.AllWords, prerequisites,
                request.Limits ?? ResolveTrialLimits(database, project, request.Scope),
                request.BaselineAssessmentIds, request.NegativeWords);
            var inputJson = JsonSerializer.Serialize(input, MotifJson.CreateOptions());
            var created = jobs.CreateTrial(jobId, workspaceKey,
                JobParserPin.Stamp(inputJson, parserPath), NowStamp(), request.BaselineAssessmentIds);
            return CommandOutcome<JobEnqueuedResponse>.Success(
                new JobEnqueuedResponse(created.JobId, TrialKind, workspaceKey));
        });
    }

    /// <summary>Snapshots effective limits for both sides of a Trial.</summary>
    internal static CommandOutcome<EnqueueTrialRequest> FreezeTrialLimits(EnqueueTrialRequest request) =>
        request.Limits is not null
            ? CommandOutcome<EnqueueTrialRequest>.Success(request)
            : ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
                CommandOutcome<EnqueueTrialRequest>.Success(request with
                {
                    Limits = ResolveTrialLimits(database, project, request.Scope),
                }));

    // The default Selection's limits as Assess resolves them, so Baseline and Trial stop a word at the same cap.
    private static TrialLimits? ResolveTrialLimits(MotifDatabase database, ProjectLocator project, string? scopeName)
    {
        var saved = new NamedSelectionRepository(database).GetDefault();
        var limits = saved?.Limits;
        if (limits is null)
        {
            try
            {
                var scopes = new ProjectConfigurationReader().Read(project).Scopes;
                var scope = scopes.FirstOrDefault(declared => declared.Name == (scopeName ?? AssessmentScopeConfiguration.DefaultName))
                    ?? scopes[0];
                limits = SelectionLimitPolicy.Estimated(scope.PerWordStepLimit);
            }
            catch (ProjectConfigurationException)
            {
                return null;
            }
        }
        return ParseRunLimits.Resolve(limits, ParseRunLimits.ReadRate(new AssessmentRepository(database)));
    }

    /// <summary>The words a Trial request would measure: its own, or those its Proposal touches; null means all.</summary>
    internal static CommandOutcome<IReadOnlyList<string>?> ResolveTrialWords(EnqueueTrialRequest request)
    {
        if (request.Words is not null || request.AllWords)
            return CommandOutcome<IReadOnlyList<string>?>.Success(request.Words);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var repository = new ProposalRepository(database);
            try
            {
                var id = ProposalCommands.NormalizeId(request.ProposalId);
                var record = repository.Get(CanonicalId.Parse(id));
                if (record.DraftName is null) (record, _) = repository.GetFinalized(CanonicalId.Parse(id));
                return CommandOutcome<IReadOnlyList<string>?>.Success(
                    DerivedTrialWords(ProposalJsonParser.Parse(CanonicalProposalJson(record))));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<IReadOnlyList<string>?>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
            }
        });
    }

    /// <summary>
    /// The words a Trial measures when its caller names none: the Default Selection (<see langword="null"/>) for a
    /// Proposal with a grammar change, since that can move any word; the touched words for a word-only Proposal,
    /// or the Default Selection when it names no word.
    /// </summary>
    public static IReadOnlyList<string>? DerivedTrialWords(SIL.Motif.Contract.Model.Proposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (ChangeVocabulary.ChangesGrammar(proposal)) return null;
        var touched = ProposalCommands.ChangedWords(proposal).ToArray();
        return touched.Length == 0 ? null : touched;
    }

    /// <summary>
    /// Polls one Dry Run job until it reaches a terminal state, binds the published anchor onto the
    /// Proposal exactly as the in-process verb used to (so <c>apply</c> keeps working), and returns it.
    /// A job still not terminal when <see cref="WaitForDryRunRequest.Timeout"/> elapses is reported as its
    /// own distinct refusal rather than as though the Dry Run had failed.
    /// </summary>
    public static CommandOutcome<DryRunProjection> WaitForDryRun(
        WaitForDryRunRequest request, CancellationToken cancellationToken = default,
        bool cancelOnTimeout = false, Action? wakeRunner = null)
    {
        var waited = JobWait.WaitAsync(request.FwDataPath, request.JobId, null, cancellationToken,
            request.Timeout, request.ProductVersion, cancelOnTimeout, wakeRunner).GetAwaiter().GetResult();
        if (!waited.Succeeded) return CommandOutcome<DryRunProjection>.Refused(waited.Refusal!);

        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var job = new JobRepository(database).Get(request.JobId);
            if (job is null) return CommandOutcome<DryRunProjection>.Refused(JobNotFound(request.JobId));
            if (job.Status != JobStatus.CompletedDryRunOnly || job.DryRunJson is null)
            {
                return CommandOutcome<DryRunProjection>.Refused(new Refusal(
                    "job.dry-run-incomplete", FailureReason.Refused,
                    "Dry Run job '" + request.JobId + "' finished as " + JobStatusJson.ToWire(job.Status) +
                    " rather than completing.",
                    Fact(("jobId", request.JobId), ("status", JobStatusJson.ToWire(job.Status)))));
            }

            try
            {
                var repository = new ProposalRepository(database);
                var id = ProposalCommands.NormalizeId(request.ProposalId);
                var canonicalId = CanonicalId.Parse(id);
                var dryRunInput = DryRunJobInput.Parse(job.InputJson);
                var proposal = dryRunInput.Proposal.Validate();
                if (proposal.ProposalId != canonicalId)
                    throw new InvalidDataException("The Dry Run job belongs to another Proposal.");
                var dryRun = ParsePublishedDryRun(job.DryRunJson);

                var completion = DryRunJobCompletion.Parse(job.ResultJson
                    ?? throw new InvalidDataException("The Dry Run has no source binding."));
                if (completion.ContentDigest != dryRun.IntentDigest)
                    throw new InvalidDataException("The Dry Run content differs from its source binding.");
                var record = repository.Get(canonicalId);
                var currentDigest = IntentDigest.Compute(ProposalJsonParser.Parse(CanonicalProposalJson(record)));
                var current = IsDryRunEvidenceCurrent(database, job.ProjectKey, currentDigest,
                    dryRun.IntentDigest, completion.BaselineToken);
                if (current) repository.SetAnchor(canonicalId, JsonSerializer.Serialize(dryRun.Anchor));

                return CommandOutcome<DryRunProjection>.Success(DryRunProjectionBuilder.Build(id, dryRun, proposal) with
                {
                    Baseline = completion.BaselineToken,
                    EvidenceCurrent = current,
                });
            }
            catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException or InvalidDataException)
            {
                return CommandOutcome<DryRunProjection>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
            }
        });
    }

    /// <summary>
    /// Polls one job until it reaches a terminal state, then returns it exactly as <see cref="Show"/>
    /// does — used by verbs, such as <c>trial</c>, whose completion has no Dry-Run-specific anchor to
    /// bind and so needs no projection of its own beyond the job's own terminal status.
    /// </summary>
    public static CommandOutcome<JobStatusResponse> WaitForJob(WaitForJobRequest request, Action? wakeRunner = null) =>
        JobWait.WaitAsync(request.FwDataPath, request.JobId, null, CancellationToken.None,
            request.Timeout, request.ProductVersion, wakeRunner: wakeRunner).GetAwaiter().GetResult();

    /// <summary>Reports what the durable store currently says about one job.</summary>
    public static CommandOutcome<JobStatusResponse> Show(ShowJobRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.JobId))
            return CommandOutcome<JobStatusResponse>.Refused(JobIdRequired());

        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var entry = new JobRepository(database).GetWithQueueOrder(request.JobId);
            if (entry is null) return CommandOutcome<JobStatusResponse>.Refused(JobNotFound(request.JobId));

            var job = entry.Value.Job;
            var (trialProgress, stageProgress) = DecodeProgress(job.ProgressJson);
            if (JobStateMachine.IsTerminal(job.Status) && stageProgress?.Stage == "waiting-for-parser-slot")
                stageProgress = null;
            return CommandOutcome<JobStatusResponse>.Success(new JobStatusResponse(job.JobId, job.ProjectKey, true,
                job.Kind, job.Status, job.Attempt, job.UpdatedUtc, job.CancellationRequested, job.FailureCategory,
                job.Version, entry.Value.QueueOrder)
            {
                TrialProgress = trialProgress,
                StageProgress = stageProgress,
                FailureCode = FailureCodeOf(job),
                FailureDetail = FailureCodeOf(job) is null ? null : FailureField(job.ResultJson, "message"),
            });
        });
    }

    private static string? FailureCodeOf(JobRecord job) =>
        job.Status == JobStatus.Failed && FailureField(job.ResultJson, "code") is { } code &&
        (code == RefusalCodes.JobParserUnavailable || RefusalCatalog.Specs.ContainsKey(code)) ? code : null;

    /// <summary>A string a failed job's result records under <paramref name="name"/>, if any.</summary>
    private static string? FailureField(string? resultJson, string name)
    {
        if (resultJson is null) return null;
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (TrialWordProgress? TrialProgress, JobStageProgress? StageProgress) DecodeProgress(
        string? progressJson)
    {
        if (progressJson is null) return (null, null);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(progressJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Stored job progress JSON is malformed.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            EnsureUniqueProperties(root);
            if (root.ValueKind != JsonValueKind.Object)
                throw InvalidProgress("the root must be an object");

            var properties = root.EnumerateObject().ToDictionary(item => item.Name, item => item.Value,
                StringComparer.Ordinal);
            var stageFields = new HashSet<string>(StringComparer.Ordinal)
                { "Stage", "Step", "Steps", "Detail" };
            var wordFields = new HashSet<string>(StringComparer.Ordinal)
                { "Completed", "Total", "CurrentWord", "StoppedWords", "SlowestWord", "PerWordLimitMs" };
            var hasStageFields = properties.Keys.Any(stageFields.Contains);
            var hasWordFields = properties.Keys.Any(wordFields.Contains);
            if (hasStageFields == hasWordFields)
                throw InvalidProgress("the object must match exactly one supported progress shape");

            var allowedFields = hasStageFields ? stageFields : wordFields;
            if (properties.Keys.Any(name => !allowedFields.Contains(name)))
                throw InvalidProgress("the object contains an unsupported progress field");

            if (hasStageFields)
            {
                var stage = ReadRequiredString(properties, "Stage");
                var step = ReadRequiredInt32(properties, "Step");
                var steps = ReadRequiredInt32(properties, "Steps");
                var detail = properties.TryGetValue("Detail", out var detailElement)
                    ? ReadNullableString(detailElement, "Detail") : null;
                if (string.IsNullOrWhiteSpace(stage) || step < 1 || steps < step)
                    throw InvalidProgress("the stage position is incoherent");
                return (null, new JobStageProgress(stage, step, steps, detail));
            }

            var completed = ReadRequiredInt32(properties, "Completed");
            var total = ReadRequiredInt32(properties, "Total");
            string? currentWord = properties.TryGetValue("CurrentWord", out var currentWordElement)
                ? ReadNullableString(currentWordElement, "CurrentWord") : null;
            if (completed < 0 || total < 0 || completed > total)
                throw InvalidProgress("the word counts are incoherent");
            ValidateWordProgressOptions(properties);
            try
            {
                var trial = JsonSerializer.Deserialize<TrialWordProgress>(progressJson, MotifJson.CreateOptions())
                    ?? throw InvalidProgress("the word progress is empty");
                return (trial with { CurrentWord = currentWord }, null);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Stored word progress has an invalid shape.", exception);
            }
        }
    }

    private static void ValidateWordProgressOptions(IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (properties.TryGetValue("StoppedWords", out var stoppedWords))
        {
            if (stoppedWords.ValueKind != JsonValueKind.Array)
                throw InvalidProgress("StoppedWords must be an array");
            foreach (var stopped in stoppedWords.EnumerateArray())
            {
                EnsureUniqueProperties(stopped);
                if (stopped.ValueKind != JsonValueKind.Object)
                    throw InvalidProgress("a stopped word must be an object");
                var fields = stopped.EnumerateObject().ToDictionary(item => item.Name, item => item.Value,
                    StringComparer.Ordinal);
                if (fields.Keys.Any(name => name is not ("Word" or "Reason" or "ElapsedMs")))
                    throw InvalidProgress("a stopped word contains an unsupported field");
                _ = ReadRequiredString(fields, "Word");
                _ = ReadRequiredString(fields, "Reason");
                _ = ReadRequiredDouble(fields, "ElapsedMs");
            }
        }

        if (properties.TryGetValue("SlowestWord", out var slowest) && slowest.ValueKind != JsonValueKind.Null)
        {
            EnsureUniqueProperties(slowest);
            if (slowest.ValueKind != JsonValueKind.Object)
                throw InvalidProgress("SlowestWord must be an object or null");
            var fields = slowest.EnumerateObject().ToDictionary(item => item.Name, item => item.Value,
                StringComparer.Ordinal);
            if (fields.Keys.Any(name => name is not ("Word" or "ElapsedMs")))
                throw InvalidProgress("SlowestWord contains an unsupported field");
            _ = ReadRequiredString(fields, "Word");
            _ = ReadRequiredDouble(fields, "ElapsedMs");
        }

        if (properties.TryGetValue("PerWordLimitMs", out var limit) && limit.ValueKind != JsonValueKind.Null &&
            (limit.ValueKind != JsonValueKind.Number || !limit.TryGetInt32(out var milliseconds) || milliseconds <= 0))
            throw InvalidProgress("PerWordLimitMs must be a positive integer or null");
    }

    private static string ReadRequiredString(IReadOnlyDictionary<string, JsonElement> properties, string name)
    {
        if (!properties.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw InvalidProgress(name + " must be a string");
        return value.GetString()!;
    }

    private static string? ReadNullableString(JsonElement value, string name) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        _ => throw InvalidProgress(name + " must be a string or null"),
    };

    private static int ReadRequiredInt32(IReadOnlyDictionary<string, JsonElement> properties, string name)
    {
        if (!properties.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result))
            throw InvalidProgress(name + " must be a 32-bit integer");
        return result;
    }

    private static double ReadRequiredDouble(IReadOnlyDictionary<string, JsonElement> properties, string name)
    {
        if (!properties.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result) || result < 0)
            throw InvalidProgress(name + " must be a finite nonnegative number");
        return result;
    }

    private static void EnsureUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw InvalidProgress("the object repeats field " + property.Name);
                EnsureUniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) EnsureUniqueProperties(item);
        }
    }

    private static InvalidDataException InvalidProgress(string reason) =>
        new("Stored job progress is invalid: " + reason + ".");

    /// <summary>
    /// The Assessments one job produced — the lookup an agent needs when it holds a Trial's job id and
    /// nothing else, having enqueued several without waiting on any of them. Reads the ids a completed
    /// Trial recorded on its own outcome and looks each one up, rather than re-deriving them from the job's
    /// input.
    /// </summary>
    public static CommandOutcome<JobAssessmentsResponse> Assessments(JobAssessmentsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.JobId))
            return CommandOutcome<JobAssessmentsResponse>.Refused(JobIdRequired());

        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var job = new JobRepository(database).Get(request.JobId);
            if (job is null) return CommandOutcome<JobAssessmentsResponse>.Refused(JobNotFound(request.JobId));

            var assessmentIds = TryReadAssessmentIds(job.ResultJson);
            if (assessmentIds is null)
            {
                return CommandOutcome<JobAssessmentsResponse>.Refused(new Refusal(
                    "job.no-assessments", FailureReason.Refused,
                    "Job '" + request.JobId + "' recorded no Assessments" +
                    (JobStateMachine.IsTerminal(job.Status)
                        ? "."
                        : "; it is still " + JobStatusJson.ToWire(job.Status) + "."),
                    Fact(("jobId", request.JobId))));
            }

            var repository = new AssessmentRepository(database);
            JobAssessmentSummary[] summaries;
            try
            {
                summaries = assessmentIds
                    .Select(id => repository.GetHeader(id) ?? throw new KeyNotFoundException("The Assessment does not exist."))
                    .Select(record => new JobAssessmentSummary(
                        record.AssessmentId, record.Assessor, record.Kind, record.SavedUtc))
                    .ToArray();
            }
            catch (KeyNotFoundException exception)
            {
                return CommandOutcome<JobAssessmentsResponse>.Refused(new Refusal(
                    "job.assessments-inconsistent", FailureReason.StoreInconsistent,
                    "The job recorded an Assessment that is missing from the project store: " + exception.Message,
                    Fact(("jobId", request.JobId))));
            }
            return CommandOutcome<JobAssessmentsResponse>.Success(
                new JobAssessmentsResponse(request.JobId, summaries));
        });
    }

    // Reads the "assessmentIds" array a completed Trial's own ResultJson carries; null for anything else.
    private static IReadOnlyList<string>? TryReadAssessmentIds(string? resultJson)
    {
        if (resultJson is null) return null;
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            if (!document.RootElement.TryGetProperty("assessmentIds", out var array) ||
                array.ValueKind != JsonValueKind.Array)
                return null;
            return array.EnumerateArray().Select(element => element.GetString()!).ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Cancels one job. A job still queued (or parked waiting for a Baseline or the project host) is
    /// moved straight to <c>cancelled</c> — nothing is running it, so no runner is needed. A running job
    /// only has its cancellation flag set; the runner that holds it reads the flag on its own heartbeat
    /// and cancels the handler's token from there, landing in the same terminal state.
    /// </summary>
    public static CommandOutcome<JobStatusResponse> Cancel(CancelJobRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var jobs = new JobRepository(database);
            var current = jobs.Get(request.JobId);
            if (current is null) return CommandOutcome<JobStatusResponse>.Refused(JobNotFound(request.JobId));
            if (JobStateMachine.IsTerminal(current.Status))
            {
                return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
                    "job.already-finished", FailureReason.Refused,
                    "Job '" + request.JobId + "' already finished as " + JobStatusJson.ToWire(current.Status) +
                    "; there is nothing to cancel.",
                    Fact(("jobId", request.JobId), ("status", JobStatusJson.ToWire(current.Status)))));
            }

            var changed = jobs.CancelActive(request.JobId);
            if (JobStateMachine.IsTerminal(changed.Status) && changed.Status != JobStatus.Cancelled)
                return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
                    "job.already-finished", FailureReason.Refused,
                    "Job '" + request.JobId + "' already finished as " + JobStatusJson.ToWire(changed.Status) +
                    "; there is nothing to cancel.",
                    Fact(("jobId", request.JobId), ("status", JobStatusJson.ToWire(changed.Status)))));

            return CommandOutcome<JobStatusResponse>.Success(new JobStatusResponse(changed.JobId, changed.ProjectKey,
                true, changed.Kind, changed.Status, changed.Attempt, changed.UpdatedUtc,
                changed.CancellationRequested, changed.FailureCategory, changed.Version));
        });
    }

    /// <summary>Starts a fresh attempt of a terminal job's lineage, claimable exactly like a new job.</summary>
    public static CommandOutcome<JobStatusResponse> Requeue(RequeueJobRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var jobs = new JobRepository(database);
            var current = jobs.Get(request.JobId);
            if (current is null) return CommandOutcome<JobStatusResponse>.Refused(JobNotFound(request.JobId));
            if (!JobStateMachine.IsTerminal(current.Status))
            {
                return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
                    "job.not-finished", FailureReason.Refused,
                    "Job '" + request.JobId + "' is still " + JobStatusJson.ToWire(current.Status) +
                    "; only a finished job can be requeued.",
                    Fact(("jobId", request.JobId), ("status", JobStatusJson.ToWire(current.Status)))));
            }

            var retried = jobs.Retry(request.JobId, current.Version);
            return CommandOutcome<JobStatusResponse>.Success(new JobStatusResponse(retried.JobId, retried.ProjectKey,
                true, retried.Kind, retried.Status, retried.Attempt, retried.UpdatedUtc,
                retried.CancellationRequested, retried.FailureCategory, retried.Version));
        });
    }

    /// <summary>Every active job across every Known project, in the order <see cref="JobClaims.Claim"/> takes it.</summary>
    public static CommandOutcome<JobQueueListResponse> ListAll(ListActiveJobsRequest request)
    {
        var entries = ReadGlobalActiveQueue(request.ProductVersion);
        return CommandOutcome<JobQueueListResponse>.Success(new JobQueueListResponse(entries.Select(entry =>
            new JobQueueEntryResponse(entry.Job.JobId, entry.Job.ProjectKey, entry.Project.FullFwDataPath,
                entry.Job.Kind, entry.Job.Status, entry.Job.Attempt, entry.Job.UpdatedUtc, entry.QueueOrder))
            .ToArray()));
    }

    /// <summary>
    /// Repositions one job in the single global queue by writing its own <c>QueueOrder</c> alone — one
    /// row, in this job's own project database, every time. A neighbour's row is never touched: it usually
    /// lives in a different project's database, and no transaction spans two SQLite files, so a write that
    /// depended on a neighbour's row too could half-complete and reorder a job nobody asked to move.
    /// </summary>
    /// <remarks>
    /// <c>--before</c>'s ordinary case is a midpoint between the named job and its global predecessor. A
    /// predecessor tied with it has no midpoint — but a tie means their relative order was already
    /// arbitrary (decided only by <c>JobId</c>, nothing the caller chose), so landing the mover just below
    /// the target, ahead of the whole tied run, satisfies "before target" without needing to touch the
    /// predecessor's row at all.
    /// </remarks>
    public static CommandOutcome<JobStatusResponse> Move(MoveJobRequest request)
    {
        if (request.Target.Kind == JobMoveKind.Before &&
            string.Equals(request.Target.BeforeJobId, request.JobId, StringComparison.Ordinal))
        {
            return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
                "job.invalid-move", FailureReason.InvalidArgument, "A job cannot be moved before itself.",
                Fact(("jobId", request.JobId))));
        }

        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var jobs = new JobRepository(database);
            var mover = jobs.GetWithQueueOrder(request.JobId);
            if (mover is null) return CommandOutcome<JobStatusResponse>.Refused(JobNotFound(request.JobId));
            if (JobStateMachine.IsTerminal(mover.Value.Job.Status))
            {
                return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
                    "job.already-finished", FailureReason.Refused,
                    "Job '" + request.JobId + "' already finished; a terminal job's position cannot be changed.",
                    Fact(("jobId", request.JobId))));
            }

            var others = ReadGlobalActiveQueue(request.ProductVersion)
                .Where(entry => !string.Equals(entry.Job.JobId, request.JobId, StringComparison.Ordinal)).ToArray();

            double newOrder;
            switch (request.Target.Kind)
            {
                case JobMoveKind.ToTop:
                    newOrder = others.Length == 0 ? mover.Value.QueueOrder : others[0].QueueOrder - 1.0;
                    break;
                case JobMoveKind.ToBottom:
                    newOrder = others.Length == 0 ? mover.Value.QueueOrder : others[^1].QueueOrder + 1.0;
                    break;
                default:
                    var targetIndex = Array.FindIndex(others,
                        entry => string.Equals(entry.Job.JobId, request.Target.BeforeJobId, StringComparison.Ordinal));
                    if (targetIndex < 0)
                    {
                        return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
                            "job.move-target-not-found", FailureReason.NotFound,
                            "No active job '" + request.Target.BeforeJobId + "' is recorded to move before.",
                            Fact(("jobId", request.Target.BeforeJobId))));
                    }
                    newOrder = QueueOrderBefore(others, targetIndex);
                    break;
            }

            var moved = jobs.SetQueueOrder(request.JobId, newOrder, mover.Value.Job.Version, NowStamp());
            return CommandOutcome<JobStatusResponse>.Success(new JobStatusResponse(moved.JobId, moved.ProjectKey,
                true, moved.Kind, moved.Status, moved.Attempt, moved.UpdatedUtc, moved.CancellationRequested,
                moved.FailureCategory, moved.Version, newOrder));
        });
    }

    /// A tie has no midpoint; landing just below the target puts the mover ahead of the whole tied run.
    private static double QueueOrderBefore(IReadOnlyList<GlobalQueueEntry> others, int targetIndex)
    {
        var target = others[targetIndex];
        if (targetIndex == 0) return target.QueueOrder - 1.0;

        var predecessor = others[targetIndex - 1];
        return predecessor.QueueOrder < target.QueueOrder
            ? (predecessor.QueueOrder + target.QueueOrder) / 2.0
            : target.QueueOrder - 0.5;
    }

    /// Every active job in every reachable Known project, in global QueueOrder-then-JobId order.
    private static IReadOnlyList<GlobalQueueEntry> ReadGlobalActiveQueue(string productVersion)
    {
        using var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot());
        var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, ParseProductVersion(productVersion));
        var entries = new List<GlobalQueueEntry>();
        foreach (var known in new KnownProjectRegistry(machine).List())
        {
            if (!File.Exists(known.FullFwDataPath)) continue;
            var locator = new ProjectLocator(known.FullFwDataPath, Path.GetFileNameWithoutExtension(known.FullFwDataPath));
            try
            {
                using var database = catalog.OpenOwned(locator);
                foreach (var entry in new JobRepository(database).ListActiveByQueueOrder(known.WorkspaceKey))
                    entries.Add(new GlobalQueueEntry(known, entry.Job, entry.QueueOrder));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException)
            {
                // Skipped, not thrown: one unreadable project must not hide every other project's queue.
            }
        }
        return entries.OrderBy(entry => entry.QueueOrder).ThenBy(entry => entry.Job.JobId, StringComparer.Ordinal)
            .ToArray();
    }

    private static Version ParseProductVersion(string productVersion) =>
        MotifProductVersion.CompatibilityVersion(productVersion);

    private static string NowStamp() => JobTimestamp.FormatUtc(DateTimeOffset.UtcNow);

    /// <summary>Returns the immutable revision or the canonical semantic content of a Draft.</summary>
    public static string CanonicalProposalJson(ProposalRecord record) => record.DraftName is null
        ? record.ProposalJson!
        : ProposalCommands.BuildProposalJson(JsonSerializer.Deserialize<DraftDocument>(record.ProposalJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The Draft has no content."));

    private static string SourceDigest(string path)
    {
        using var source = File.OpenRead(path);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(source));
    }


    private static Refusal JobNotFound(string jobId) =>
        new("job.not-found", FailureReason.NotFound,
            "No job '" + jobId + "' is recorded for this project.", Fact(("jobId", jobId)));

    private static Refusal JobIdRequired() =>
        new("job.invalid-id", FailureReason.InvalidArgument, "A job id is required.");

    private static Dictionary<string, string> Fact(params (string Key, string? Value)[] entries)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            if (value is not null) facts[key] = value;
        }
        return facts;
    }

    /// <summary>One active job read while assembling the cross-project queue view.</summary>
    private readonly record struct GlobalQueueEntry(KnownProjectRecord Project, JobRecord Job, double QueueOrder);

    // Reads a published Dry Run's JSON back into the model the renderer takes.
    internal static DryRunModel ParsePublishedDryRun(string dryRunJson)
    {
        using var document = JsonDocument.Parse(dryRunJson);
        var root = document.RootElement;
        var anchor = JsonSerializer.Deserialize<BoundDryRunAnchor>(
            root.GetProperty("anchor").GetRawText(), MotifJson.CreateOptions())!;
        return new DryRunModel(
            root.GetProperty("intentDigest").GetString()!,
            root.GetProperty("baselineNote").GetString()!,
            ParseExpectedEffects(root.GetProperty("expectedEffects")),
            root.GetProperty("effectDigest").GetString()!,
            anchor);
    }

    /// <summary>Checks whether saved Dry Run content and Baseline evidence are current.</summary>
    /// <param name="database">The project's Motif store.</param>
    /// <param name="projectKey">The project identity recorded by the job.</param>
    /// <param name="currentIntentDigest">The current Proposal content digest, if available.</param>
    /// <param name="evidenceIntentDigest">The Proposal content digest recorded by the Dry Run.</param>
    /// <param name="evidenceBaseline">The Baseline measured by the Dry Run.</param>
    /// <returns>Whether both the Proposal content and the Baseline evidence still match.</returns>
    internal static bool IsDryRunEvidenceCurrent(MotifDatabase database, string projectKey,
        string? currentIntentDigest, string evidenceIntentDigest, BaselineToken evidenceBaseline) =>
        string.Equals(currentIntentDigest, evidenceIntentDigest, StringComparison.Ordinal) &&
        new BaselineRepository(database).IsCurrentEvidence(projectKey, evidenceBaseline);

    private static IReadOnlyList<ExpectedEffect> ParseExpectedEffects(JsonElement array)
    {
        var effects = new List<ExpectedEffect>();
        foreach (var element in array.EnumerateArray())
        {
            effects.Add(new ExpectedEffect(
                CanonicalId.Parse(element.GetProperty("canonicalId").GetString()!),
                element.GetProperty("field").GetString()!,
                ReadAlternatives(element.GetProperty("before")),
                ReadAlternatives(element.GetProperty("after")),
                element.TryGetProperty("preview", out var preview) ? preview.Clone() : null));
        }
        return effects;
    }

    private static IReadOnlyDictionary<string, string> ReadAlternatives(JsonElement alternatives)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in alternatives.EnumerateObject())
            map[property.Name] = property.Value.GetString() ?? "";
        return map;
    }
}

/// <summary>Which end of the global queue <c>jobs move</c> targets.</summary>
public enum JobMoveKind
{
    ToTop,
    ToBottom,
    Before
}

/// <summary>One <c>jobs move</c> invocation's destination: an end of the queue, or immediately before a named job.</summary>
public readonly record struct JobMoveTarget(JobMoveKind Kind, string? BeforeJobId)
{
    public static JobMoveTarget ToTop() => new(JobMoveKind.ToTop, null);
    public static JobMoveTarget ToBottom() => new(JobMoveKind.ToBottom, null);
    public static JobMoveTarget Before(string jobId) => new(JobMoveKind.Before, jobId);
}
