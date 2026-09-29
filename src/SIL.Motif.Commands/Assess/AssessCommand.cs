using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Assess;

/// <summary>
/// Measures the current Baseline synchronously (design decision 4): ensures a Baseline exists, composes a
/// Selection from whichever of the four agreed sources were asked for, runs the Assessor over it through the
/// PanGloss batch invocation, records every produced Assessment, and reads its statistics cache as a text summary.
/// </summary>
/// <remarks>
/// <para>
/// This command never wakes the durable job runner — it captures, measures, and returns within one call,
/// the same synchronous shape <see cref="BaselineCaptureCommand"/> already established. A cancelled run
/// records nothing: <see cref="RetainedInvocationRepository.Record"/> is called only after the Assessor has already
/// returned, so a cancellation raised while it is still running never leaves a partial Assessment behind.
/// </para>
/// <para>
/// Shares its interpretation of <c>ProducedAssessment</c> with <c>TrialJobHandler</c> through
/// <see cref="AssessmentMaterial"/> rather than forking it: two independent readings of the same raw shape
/// could silently drift apart.
/// </para>
/// </remarks>
public static class AssessCommand
{
    // assess has no configured query text; SelectionComposer already resolved the words themselves.
    private const string ScopeQuery = "assess";

    private const string TokeniserName = "none";
    private const string TokeniserVersion = "1";

    private static readonly IReadOnlyList<AssessmentKind> CollectedKinds =
        [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming];

    /// <summary>Measures the project, resolving the managed root the real installation uses.</summary>
    public static CommandOutcome<AssessCommandResponse> Assess(
        AssessRequest request, Action<AssessmentProgress>? onProgress = null,
        CancellationToken cancellationToken = default) =>
        Assess(request, RunnerOptions.ResolveRoot(), onProgress, cancellationToken);

    /// <summary>
    /// Measures the project under an explicitly supplied managed root, with the parser the environment
    /// locates. The CLI calls the overload without a root; the window calls the one that also takes the
    /// parser; this one lets a test supply its own disposable root.
    /// </summary>
    public static CommandOutcome<AssessCommandResponse> Assess(
        AssessRequest request, string managedRoot, Action<AssessmentProgress>? onProgress = null,
        CancellationToken cancellationToken = default) =>
        Assess(request, managedRoot, PanGlossExecutable.TryLocate(), onProgress, cancellationToken);

    /// <summary>Measures a project under an explicit managed root with an explicitly selected parser.</summary>
    /// <param name="parserPath">The parser to run, or <see langword="null"/> when none is available.</param>
    public static CommandOutcome<AssessCommandResponse> Assess(
        AssessRequest request, string managedRoot, string? parserPath,
        Action<AssessmentProgress>? onProgress, CancellationToken cancellationToken)
    {
        var ownership = WorkspaceOwnership.Bootstrap(managedRoot);
        using var invoker = new PanGlossInvoker(parserPath);
        var assessor = new LazyPanGlossAssessor(() => new PanGlossAssessor(new StatsCacheStore(ownership), invoker));
        return Run(request, managedRoot, assessor, invoker, onProgress, cancellationToken);
    }

    /// <summary>
    /// Measures the project against explicitly supplied collaborators — a fake Assessor and a fake invoker
    /// stand in for a real PanGloss in tests. Admission and containment are the invoker's, so this command
    /// holds no queue and no governor.
    /// </summary>
    internal static CommandOutcome<AssessCommandResponse> Run(
        AssessRequest request, string managedRoot, IAssessor assessor, IPanGlossInvoker invoker,
        Action<AssessmentProgress>? onProgress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assessor);
        ArgumentNullException.ThrowIfNull(invoker);

        return ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, project) =>
        {
            if (cancellationToken.IsCancellationRequested)
                return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
            if (request.PerWordLimitMs is <= 0)
                return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                    "assess.invalid-limit", FailureReason.InvalidArgument, "A per-word time limit must be positive."));
            if (invoker.ExecutableMissing)
                return CommandOutcome<AssessCommandResponse>.Refused(cancellationToken.IsCancellationRequested
                    ? Cancelled(request.ProjectPath)
                    : ParserUnavailable(request.ProjectPath, PanGlossExecutable.NotFoundMessage, executableMissing: true));
            AssessmentScopeConfiguration configured;
            try
            {
                // The project's declared default scope sets the limits a run is held to, as `config show` reports.
                var scopes = new ProjectConfigurationReader().Read(project).Scopes;
                configured = scopes.FirstOrDefault(declared => declared.Name == AssessmentScopeConfiguration.DefaultName)
                    ?? scopes[0];
            }
            catch (ProjectConfigurationException exception)
            {
                return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                    "config.invalid", FailureReason.Refused, exception.Message));
            }

            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var baselines = new BaselineRepository(database);
            var assessments = new AssessmentRepository(database);
            var retainedInvocations = new RetainedInvocationRepository(database);
            var namedSelections = new NamedSelectionRepository(database);

            onProgress?.Invoke(new AssessmentProgress(
                AssessmentStage.Capturing, 0, null, "Ensuring a current Baseline exists..."));
            var baselineOutcome = EnsureBaseline(request.ProjectPath, managedRoot, project, workspaceKey, baselines);
            if (!baselineOutcome.Succeeded)
                return CommandOutcome<AssessCommandResponse>.Refused(baselineOutcome.Refusal!);
            var baseline = baselineOutcome.Value!;

            SelectionRequest selectionRequest;
            string? namedSelection = null;
            int? defaultPerWordLimitMs = null;
            var usesDefaultSelection = request.Selection is null;
            if (request.Selection is null)
            {
                var saved = namedSelections.GetDefault();
                if (saved is null)
                    return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                        "selection.default-missing", FailureReason.Refused,
                        "No default Selection is saved for this project. Save one with `motif selection set-default` first."));
                namedSelection = saved.Name;
                defaultPerWordLimitMs = saved.PerWordLimitMs;
                selectionRequest = new SelectionRequest(false, saved.TextIds, saved.AddedWords, false, null,
                    PerWordStepLimit: saved.PerWordStepLimit);
            }
            else
            {
                selectionRequest = request.Selection;
            }

            onProgress?.Invoke(new AssessmentProgress(
                AssessmentStage.SelectingWords, 0, null, "Composing the Selection..."));
            SelectionComposition composition;
            using (var cache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath))
            {
                var composed = SelectionComposer.Compose(
                    cache, selectionRequest, assessments, JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions()));
                if (!composed.Succeeded)
                    return CommandOutcome<AssessCommandResponse>.Refused(composed.Refusal!);
                composition = composed.Value!;
                if (namedSelection is not null)
                    composition = composition with { Selection = composition.Selection with { Name = namedSelection } };
            }

            AssessmentScope scope;
            var exportedCandidate = Path.GetDirectoryName(baseline.FwDataPath)!;
            IReadOnlyList<ProducedAssessment> produced;
            try
            {
                var supportedKinds = assessor.SupportedKinds;
                var collected = supportedKinds.Contains(AssessmentKind.Correctness)
                    ? CollectedKinds.Append(AssessmentKind.Correctness).ToArray() : CollectedKinds;
                var unsupported = collected.Where(kind => !supportedKinds.Contains(kind)).ToArray();
                if (unsupported.Length > 0)
                    return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                        "assess.unsupported-kind", FailureReason.Refused,
                        $"The Assessor does not declare required Assessment kind '{unsupported[0]}'.",
                        new Dictionary<string, string> { ["kind"] = unsupported[0].ToString() }));
                var stepLimit = request.PerWordStepLimit ?? selectionRequest.PerWordStepLimit ?? configured.PerWordStepLimit;
                TimeSpan? timeLimit = request.PerWordLimitMs is { } ms
                    ? TimeSpan.FromMilliseconds(ms)
                    : stepLimit.IsUnbounded ? null
                    : usesDefaultSelection
                        ? defaultPerWordLimitMs is { } savedMs ? TimeSpan.FromMilliseconds(savedMs) : null
                        : EstimatePerWordTimeLimit(assessments, stepLimit);
                scope = new AssessmentScope(composition.Selection.Words, collected, timeLimit, stepLimit);
                onProgress?.Invoke(new AssessmentProgress(
                    AssessmentStage.Parsing, 0, composition.Selection.Words.Count, "Parsing the Selection..."));
                produced = assessor.ProduceAsync(scope, exportedCandidate, cancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
            }
            catch (AssessorUnavailableException ex)
            {
                return CommandOutcome<AssessCommandResponse>.Refused(
                    ParserUnavailable(request.ProjectPath, ex.Message, ex.ExecutableMissing));
            }
            catch (AssessorRefusalException ex)
            {
                return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                    "assess.unsupported-kind", FailureReason.Refused, ex.Message,
                    new Dictionary<string, string> { ["kind"] = ex.Kind.ToString() }));
            }

            if (produced is null)
            {
                return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                    "assess.measurements-incomplete", FailureReason.StoreInconsistent,
                    "The Assessor returned no measurement collection."));
            }
            var artifactLeases = produced.Select(item => item.ArtifactLease).OfType<AssessmentArtifactLease>().Distinct().ToArray();
            try
            {
                var expectedKinds = (scope.Collect.Count == 0 ? assessor.SupportedKinds : scope.Collect)
                    .ToArray();
                if (produced is null || produced.Count != expectedKinds.Length ||
                    produced.Select(item => item.Kind).Distinct().Count() != produced.Count ||
                    expectedKinds.Except(produced.Select(item => item.Kind)).Any() ||
                    produced.Select(item => item.Kind).Except(expectedKinds).Any())
                {
                    return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                        "assess.measurements-incomplete", FailureReason.StoreInconsistent,
                        "The Assessor did not return exactly one measurement for every required kind."));
                }
                if (cancellationToken.IsCancellationRequested)
                    return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
                var scopeJson = ScopeCodec.Write(
                    new StoredScope.Trial(ScopeQuery, scope.Words, scope.Collect, scope.PerWordLimit, scope.PerWordStepLimit));
                var scopeDigest = AssessmentMaterial.Digest(scopeJson);
                var baselineTokenJson = JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions());
                var savedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

                var assessmentIds = new List<string>();
                var pendingRecords = new List<NewAssessmentRecord>();
                string? statsCachePath = null;
                NewAssessmentRecord? statisticsRecord = null;
                foreach (var item in produced)
                {
                    var assessmentId = CanonicalId.Mint("assessment/").Value;
                    var record = AssessmentMaterial.ToRecord(item, assessmentId, proposalId: null,
                        proposalIntentDigest: null, assessor.Name, scopeJson, scopeDigest, TokeniserName,
                        TokeniserVersion, baselineTokenJson, composition.Selection) with { SavedUtc = savedUtc };
                    pendingRecords.Add(record);
                    assessmentIds.Add(assessmentId);
                    if (record.CachePath is not null)
                    {
                        statsCachePath = record.CachePath;
                        statisticsRecord = record;
                    }
                }

                var invocations = pendingRecords.Select(record => record.Invocation).Distinct().ToArray();
                var invocationCandidates = invocations.OfType<BatchInvocationEvidence>().Distinct().ToArray();
                var invocation = invocationCandidates.Length == 1 ? invocationCandidates[0] : null;
                if (invocations.Any(item => item is null) ||
                    invocationCandidates.Length != 1 ||
                    invocation is null ||
                    pendingRecords.Any(record => record.GrammarSourceSha256 != invocation.SourceBytesSha256))
                {
                    return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                        "assess.invocation-inconsistent", FailureReason.Refused,
                        "The collected Assessments do not share one non-null invocation evidence record " +
                        "and its source-byte digest."));
                }

                onProgress?.Invoke(new AssessmentProgress(
                    AssessmentStage.ReadingStatistics, 0, null, "Reading PanGloss's statistics..."));
                string summaryMarkdown;
                // Require one batch statistics row for each resolved word before recording.
                Dictionary<string, (int? Attempts, int? Passes)>? wordStats = null;
                var objectTimings = new List<AssessmentObjectTiming>();
                if (statsCachePath is null)
                {
                    summaryMarkdown = "(no per-object statistics were collected)" + Environment.NewLine;
                }
                else
                {
                    if (statisticsRecord?.Invocation is null || statisticsRecord.CacheDigest is null)
                        return CommandOutcome<AssessCommandResponse>.Refused(ParserUnavailable(
                            request.ProjectPath, "The statistics measurement has no retained invocation evidence."));
                    StatsEvidenceReplay replay;
                    try
                    {
                        replay = StatsEvidenceReplay.Create(
                            statisticsRecord.Invocation, statsCachePath, statisticsRecord.CacheDigest);
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                    {
                        return CommandOutcome<AssessCommandResponse>.Refused(ParserUnavailable(request.ProjectPath, exception.Message));
                    }
                    using var replayLease = replay;
                    PanGlossBatchStatistics batchStatistics;
                    try
                    {
                        batchStatistics = PanGlossBatchStatisticsReader.Read(
                            replay.CachePath, composition.Selection.Words);
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or
                        UnauthorizedAccessException or SqliteException)
                    {
                        return CommandOutcome<AssessCommandResponse>.Refused(
                            ParserUnavailable(request.ProjectPath, exception.Message));
                    }
                    wordStats = batchStatistics.Words.ToDictionary(pair => pair.Key,
                        pair => ((int?)pair.Value.Attempts, (int?)pair.Value.Passes), StringComparer.Ordinal);
                    objectTimings.AddRange(batchStatistics.ObjectTimings.Select(row => new AssessmentObjectTiming(
                        row.Kind, row.Object, row.Word, row.Attempts, row.Passes, row.ElapsedMs)));
                    summaryMarkdown = $"Batch statistics: {batchStatistics.Words.Count} words; " +
                        $"{batchStatistics.ObjectTimings.Count} object timing rows.";
                }

                if (cancellationToken.IsCancellationRequested)
                    return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
                using var namingCache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
                var projectName = Path.GetFileNameWithoutExtension(request.ProjectPath);
                var wordContext = ReadProjectWordContext(namingCache, composition.Selection.Words,
                    composition.Descriptor.TextIds);
                // Named once and recorded, so a later read of the stored words glosses them as this run does.
                var namedMissed = new Dictionary<string, ParserReading[]?>(StringComparer.Ordinal);
                ParserReading[]? NameMissed(string word, WordCorrectness? correctness)
                {
                    if (namedMissed.TryGetValue(word, out var named)) return named;
                    return namedMissed[word] = correctness?.Unmatched
                        .Select(index => correctness.Expectations[index])
                        .Select(missed => ReadStoredAnalysis(namingCache, projectName, missed, ReadingGrade.Approved))
                        .ToArray();
                }
                pendingRecords = pendingRecords.Select(record => record with
                {
                    Words = record.Words.Select(word => word with
                    {
                        Morphology = word.Morphology is { } morphology &&
                            wordStats is not null && wordStats.TryGetValue(word.Word, out var attempts) &&
                            attempts.Attempts is { } count
                                ? morphology with { Attempts = count }
                                : word.Morphology,
                        ProjectStanding = wordContext.Standings.GetValueOrDefault(word.Word),
                        OccurrenceCount = wordContext.HasTextSelection
                            ? wordContext.OccurrencesByWord.GetValueOrDefault(word.Word) : null,
                        ReadingGrades = word.Morphology is null ? null : GradeReadings(word.Morphology.Analyses,
                            wordContext.Approved.GetValueOrDefault(word.Word) ?? Array.Empty<ApprovedMorphology>(),
                            wordContext.Rejected.GetValueOrDefault(word.Word) ?? Array.Empty<ApprovedMorphology>(),
                            wordContext.Candidates.GetValueOrDefault(word.Word) ?? Array.Empty<ApprovedMorphology>()),
                        MissedApprovedCount = word.Correctness?.Unmatched.Count,
                        MissedApproved = NameMissed(word.Word, word.Correctness),
                    }).ToArray(),
                    ObjectTimings = record.Kind == AssessmentKind.ParseTime.ToStoredKind()
                        ? objectTimings : record.ObjectTimings,
                }).ToList();
                var currentBaseline = baselines.GetCurrent(workspaceKey);
                if (currentBaseline is null || currentBaseline.Token != baseline.Token)
                    return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                        "assess.baseline-changed", FailureReason.StoreInconsistent,
                        "The Baseline changed while the Assessment was running."));
                var retained = new RetainedInvocationRecord(
                    invocation.InvocationId, workspaceKey, baseline.Token,
                    Path.GetDirectoryName(baseline.FwDataPath)!, baseline.FwDataPath,
                    currentBaseline.SourceLastWriteUtc, currentBaseline.PublishedUtc,
                    DateTimeOffset.ParseExact(savedUtc, "O", CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind),
                    composition.Descriptor, assessor.Name, scopeJson, scopeDigest, invocation.InvocationId,
                    pendingRecords.Select(record => new RetainedInvocationMember(record.Kind, record.AssessmentId))
                        .ToArray());
                retainedInvocations.Record(retained, pendingRecords);
                foreach (var lease in artifactLeases) lease.Retain();

                var timing = produced.FirstOrDefault(item => item.Kind == AssessmentKind.ParseTime);
                var words = timing?.Raw is AssessmentRaw.Batch batch
                    ? batch.Analysis.Words.Select(word => AssessmentWordRows.Row(word.Word,
                        word.Outcome.ToStoredOutcome(), word.ElapsedMs, word.Signature, word.Morphology,
                        word.Correctness)).ToArray()
                    : Array.Empty<AssessmentWordResult>();
                var completionSummary = AssessmentWordRows.CompletionSummary(words);
                summaryMarkdown = RenderSummaryMarkdown(completionSummary, summaryMarkdown);
                var grammarWarnings = invocation.GrammarWarningLines is { Count: > 0 } warningLines
                    ? warningLines : null;
                if (words.Length > 0)
                {
                    words = words.Select(word =>
                    {
                        var readings = word.Morphology is null
                            ? null : ParserReadingReader.Read(namingCache, projectName, word.Morphology);
                        var readingGrades = word.Morphology is null ? null : GradeReadings(word.Morphology.Analyses,
                            wordContext.Approved.GetValueOrDefault(word.Word) ?? Array.Empty<ApprovedMorphology>(),
                            wordContext.Rejected.GetValueOrDefault(word.Word) ?? Array.Empty<ApprovedMorphology>(),
                            wordContext.Candidates.GetValueOrDefault(word.Word) ?? Array.Empty<ApprovedMorphology>());
                        var projectStanding = wordContext.Standings.GetValueOrDefault(word.Word);
                        var missedApproved = NameMissed(word.Word, word.Correctness);
                        var candidates = wordContext.Candidates.GetValueOrDefault(word.Word) ?? Array.Empty<ApprovedMorphology>();
                        var approved = wordContext.Approved.GetValueOrDefault(word.Word) ?? Array.Empty<ApprovedMorphology>();
                        var nonApproved = candidates.Select(candidate => (Analysis: candidate, Opinion: ReadingGrade.Candidate))
                            .Concat((wordContext.Rejected.GetValueOrDefault(word.Word) ?? Array.Empty<ApprovedMorphology>())
                                .Select(rejected => (Analysis: rejected, Opinion: ReadingGrade.Disapproved)))
                            .ToArray();
                        var expectedAnalysis = approved.FirstOrDefault() is { } approvedAnalysis
                            ? ReadStoredAnalysis(namingCache, projectName, approvedAnalysis, ReadingGrade.Approved)
                            : nonApproved.Length == 1
                                ? ReadStoredAnalysis(namingCache, projectName, nonApproved[0].Analysis, nonApproved[0].Opinion)
                                : null;
                        var stats = wordStats is not null && wordStats.TryGetValue(word.Word, out var found) ? found : ((int?)null, (int?)null);
                        var row = word with
                        {
                            Readings = readings,
                            TryWordLink = FieldWorksLinks.ForWordform(namingCache, projectName, word.Word),
                            ReadingGrades = readingGrades,
                            ProjectStanding = projectStanding,
                            OccurrenceCount = wordContext.HasTextSelection
                                ? wordContext.OccurrencesByWord.GetValueOrDefault(word.Word) : null,
                            MissedApproved = missedApproved,
                            ExpectedAnalysis = expectedAnalysis,
                            Attempts = stats.Item1,
                            Passes = stats.Item2,
                        };
                        return row with { FixFirst = AssessmentWordRows.FixFirst(row) };
                    }).ToArray();
                }

                onProgress?.Invoke(new AssessmentProgress(
                    AssessmentStage.Complete, assessmentIds.Count, assessmentIds.Count, completionSummary));
                return CommandOutcome<AssessCommandResponse>.Success(
                    new AssessCommandResponse(baseline, composition.Projection, assessmentIds, summaryMarkdown)
                    {
                        Words = words,
                        CompletionSummary = completionSummary,
                        GrammarWarnings = grammarWarnings,
                        CorrectnessStatus = words.Any(word => word.Correctness is not null)
                            ? $"{words.Sum(word => word.Correctness?.Matched ?? 0)}/" +
                              $"{words.Sum(word => word.Correctness?.Expected ?? 0)} approved readings matched; " +
                              $"{words.Count(word => word.Correctness?.Status == "covered")} words covered; " +
                              $"{words.Count(word => word.Correctness?.Status == "unmatched")} unmatched; " +
                                $"{words.Count(word => word.Correctness?.Unavailable.Count > 0 || word.Morphology?.InvalidShape == true)} with unavailable evidence; " +
                                $"{words.Count(word => word.Correctness?.Expected == 0)} without approved expectations. " +
                              "Search completion is reported separately for each word."
                            : "Correctness unavailable: this Assessment did not collect approved morphology comparisons.",
                        Measurements = pendingRecords.Select(record => new ProducedAssessmentReference(
                            record.AssessmentId, record.Kind,
                            record.Invocation?.InvocationId ?? throw new InvalidDataException(
                                "An Assessment is missing its invocation evidence."))).ToArray(),
                        InvocationId = invocation.InvocationId,
                        SelectionDescriptor = composition.Descriptor,
                    });
            }
            finally
            {
                foreach (var lease in artifactLeases) lease.Dispose();
            }
        });
    }

    // No Baseline yet: capture and publish one. One already current: reuse it rather than recapturing.
    private static CommandOutcome<BaselineCaptureResponse> EnsureBaseline(string projectPath, string managedRoot,
        ProjectLocator project, string workspaceKey, BaselineRepository baselines)
    {
        var current = baselines.GetCurrent(workspaceKey);
        if (current is null)
            return BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);

        var held = File.Exists(project.FullFwDataPath + ".lock");
        return CommandOutcome<BaselineCaptureResponse>.Success(new BaselineCaptureResponse(
            current.Token, current.FwDataPath, current.SourceLastWriteUtc, held, ReusedExistingBytes: true));
    }

    private static Refusal ParserUnavailable(string projectPath, string message, bool executableMissing = false) => new(
        "assess.parser-unavailable", FailureReason.Refused, message,
        ParserNotFoundFact.Mark(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["projectPath"] = projectPath }, executableMissing));

    private static TimeSpan? EstimatePerWordTimeLimit(AssessmentRepository assessments, StepCap stepLimit)
    {
        var latest = assessments.ListByKind(AssessmentKind.ParseTime.ToStoredKind()).LastOrDefault();
        var rate = StepLimitEstimator.FromAssessment(
            latest is null ? null : assessments.Get(latest.AssessmentId));
        var estimate = StepLimitEstimator.Calculate(stepLimit, rate);
        return estimate?.PerWordTimeLimitMs is { } milliseconds && milliseconds <= int.MaxValue
            ? TimeSpan.FromMilliseconds(milliseconds)
            : null;
    }

    private static Refusal Cancelled(string projectPath) => new(
        "assessment.cancelled", FailureReason.Cancelled,
        "The Assessment run was cancelled; no Assessments were recorded.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["projectPath"] = projectPath });

    private static ProjectWordContext ReadProjectWordContext(
        LcmCache cache, IReadOnlyList<string> words, IReadOnlyList<Guid> textIds)
    {
        var approved = ApprovedMorphologyReader.Read(cache);
        var rejected = ApprovedMorphologyReader.ReadDisapproved(cache);
        var candidates = ApprovedMorphologyReader.ReadCandidates(cache);
        var misspelled = ApprovedMorphologyReader.ReadIncorrectSpellings(cache);
        var standings = words.ToDictionary(word => word, word => ProjectStandings.Of(
            approved.GetValueOrDefault(word)?.Count ?? 0,
            candidates.GetValueOrDefault(word)?.Count ?? 0,
            rejected.GetValueOrDefault(word)?.Count ?? 0,
            misspelled.Contains(word)), StringComparer.Ordinal);
        var occurrences = textIds.Count == 0
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : TextOccurrenceReader.Read(cache, textIds).OccurrencesByWord;
        return new ProjectWordContext(standings, occurrences, textIds.Count > 0, approved, rejected, candidates);
    }

    internal static string RenderSummaryMarkdown(string completionSummary, string statisticsOutput) =>
        completionSummary + Environment.NewLine + Environment.NewLine +
        "```" + Environment.NewLine + statisticsOutput + "```" + Environment.NewLine;

    // One grade per produced analysis, in Readings' own order, using the same match Correctness uses.
    private static IReadOnlyList<string> GradeReadings(
        IReadOnlyList<ParseAnalysis> analyses, IReadOnlyList<ApprovedMorphology> approved,
        IReadOnlyList<ApprovedMorphology> disapproved, IReadOnlyList<ApprovedMorphology> candidates) =>
        analyses.Select(analysis => approved.Any(expected => MorphologyCorrectness.Matches(analysis, expected)) ? ReadingGrade.Approved
            : disapproved.Any(expected => MorphologyCorrectness.Matches(analysis, expected)) ? ReadingGrade.Disapproved
            : candidates.Any(expected => MorphologyCorrectness.Matches(analysis, expected)) ? ReadingGrade.Candidate
            : ReadingGrade.NoOpinion).ToArray();

    private static ParserReading ReadStoredAnalysis(
        LcmCache cache, string projectName, ApprovedMorphology analysis, string opinion)
    {
        var morphs = analysis.Morphs.Select(morph =>
            new ParseMorph(morph.Form, morph.Msa, morph.InflType, GuessedString: null)).ToArray();
        return new ParserReading(ParserReadingReader.ReadMorphs(cache, projectName, morphs))
        {
            StoredAnalysisId = analysis.SourceAnalysisId,
            StoredAnalysisOpinion = opinion,
        };
    }

    private static string ResolveProductVersion() => MotifProductVersion.CurrentText;

    private sealed record ProjectWordContext(
        IReadOnlyDictionary<string, string> Standings,
        IReadOnlyDictionary<string, int> OccurrencesByWord,
        bool HasTextSelection,
        IReadOnlyDictionary<string, IReadOnlyList<ApprovedMorphology>> Approved,
        IReadOnlyDictionary<string, IReadOnlyList<ApprovedMorphology>> Rejected,
        IReadOnlyDictionary<string, IReadOnlyList<ApprovedMorphology>> Candidates);
}

/// <summary>
/// Defers parser discovery until the command has validated the project and Selection. Callers must read
/// <see cref="SupportedKinds"/> or call <see cref="ProduceAsync"/> only after that validation. A discovery
/// failure is surfaced as <see cref="AssessorUnavailableException"/>.
/// </summary>
internal sealed class LazyPanGlossAssessor : IAssessor
{
    private readonly Func<IAssessor> _factory;
    private IAssessor? _built;

    public LazyPanGlossAssessor(Func<IAssessor> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc />
    public string Name => PanGlossAssessor.AssessorName;

    /// <inheritdoc />
    public IReadOnlyList<AssessmentKind> SupportedKinds => Resolve().SupportedKinds;

    /// <inheritdoc />
    public Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
        AssessmentScope scope, string exportedCandidate, CancellationToken cancellationToken) =>
        Resolve().ProduceAsync(scope, exportedCandidate, cancellationToken);

    // A missing parser is the Assessor's own unavailability, not a fresh failure mode this wrapper invents.
    private IAssessor Resolve()
    {
        try
        {
            return _built ??= _factory();
        }
        catch (ParserUnavailableException exception)
        {
            throw new AssessorUnavailableException(Name, exception.Message)
            {
                ExecutableMissing = exception.ExecutableMissing,
            };
        }
    }
}
