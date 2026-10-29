using SIL.Motif.Host.Baselines;
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
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
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
using SIL.Motif.Host.Store;
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
/// the same synchronous shape <see cref="BaselineCaptureCommand"/> already established. It prepares the full
/// response before recording and checks cancellation inside the transaction, so cancellation before commit
/// leaves no retained invocation or member Assessment behind.
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
    /// <param name="request">The project and Selection to measure.</param>
    /// <param name="managedRoot">The root holding this project's managed workspace.</param>
    /// <param name="parserPath">The parser to run, or <see langword="null"/> when none is available.</param>
    /// <param name="onProgress">Receives Assessment progress, when supplied.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    public static CommandOutcome<AssessCommandResponse> Assess(
        AssessRequest request, string managedRoot, string? parserPath,
        Action<AssessmentProgress>? onProgress, CancellationToken cancellationToken)
        => AssessWithLimits(request, managedRoot, parserPath, onProgress, cancellationToken, null);

    internal static CommandOutcome<AssessCommandResponse> AssessWithLimits(
        AssessRequest request, string managedRoot, string? parserPath,
        Action<AssessmentProgress>? onProgress, CancellationToken cancellationToken, TrialLimits? limits)
    {
        var ownership = WorkspaceOwnership.Bootstrap(managedRoot);
        using var invoker = new PanGlossInvoker(parserPath);
        var assessor = new LazyPanGlossAssessor(() => new PanGlossAssessor(new StatsCacheStore(ownership), invoker));
        return RunCore(request, managedRoot, assessor, invoker, onProgress, cancellationToken, null, limits);
    }

    /// <summary>
    /// Measures the project against explicitly supplied collaborators — a fake Assessor and a fake invoker
    /// stand in for a real PanGloss in tests. The project store admits one parse; the invoker provides
    /// machine admission and process containment.
    /// </summary>
    internal static CommandOutcome<AssessCommandResponse> Run(
        AssessRequest request, string managedRoot, IAssessor assessor, IPanGlossInvoker invoker,
        Action<AssessmentProgress>? onProgress, CancellationToken cancellationToken) =>
        RunCore(request, managedRoot, assessor, invoker, onProgress, cancellationToken, null);

    /// <summary>Measures within a caller-owned admission for this project, shared with its later parsing.</summary>
    internal static CommandOutcome<AssessCommandResponse> RunAdmitted(
        AssessRequest request, string managedRoot, IAssessor assessor, IPanGlossInvoker invoker,
        Action<AssessmentProgress>? onProgress, CancellationToken cancellationToken, ProjectParseLease admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        return RunCore(request, managedRoot, assessor, invoker, onProgress, cancellationToken, admission);
    }

    private static CommandOutcome<AssessCommandResponse> RunCore(
        AssessRequest request, string managedRoot, IAssessor assessor, IPanGlossInvoker invoker,
        Action<AssessmentProgress>? onProgress, CancellationToken cancellationToken, ProjectParseLease? admission, TrialLimits? frozenLimits = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assessor);
        ArgumentNullException.ThrowIfNull(invoker);

        return ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, project) =>
        {
            if (cancellationToken.IsCancellationRequested)
                return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
            using var parseLease = admission is null ? ProjectParseLease.TryAcquire(database) : null;
            if (admission is null && parseLease is null)
                return CommandOutcome<AssessCommandResponse>.Refused(ProjectParseLease.BusyRefusal());
            if (request.PerWordLimitMs is <= 0)
                return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                    "assess.invalid-limit", FailureReason.InvalidArgument, "A per-word time limit must be positive."));
            if (invoker.ExecutableMissing)
            {
                var refusal = cancellationToken.IsCancellationRequested ? Cancelled(request.ProjectPath)
                    : ParserUnavailable(request.ProjectPath, PanGlossExecutable.NotFoundMessage, executableMissing: true);
                var current = new BaselineRepository(database).GetCurrentPublished(ProjectWorkspaceKey.Compute(project));
                if (current is not null && refusal.Reason != FailureReason.Cancelled)
                    new ParserRefusalRepository(database).Save(current.Token, refusal);
                return CommandOutcome<AssessCommandResponse>.Refused(refusal);
            }
            AssessmentScopeConfiguration configured;
            try
            {
                // The project's declared default scope sets the kinds and limits a run is held to, as `config show` reports.
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
            SelectionParsingLimits? defaultLimits = namedSelections.GetDefault()?.Limits;
            if (request.Selection is null)
            {
                var saved = namedSelections.GetDefault();
                if (saved is null)
                    return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                        "selection.default-missing", FailureReason.Refused,
                        "No default Selection is saved for this project. Save one with `motif selection set-default` first."));
                namedSelection = saved.Name;
                defaultLimits = saved.Limits;
                selectionRequest = new SelectionRequest(false, saved.TextIds, saved.AddedWords, false, null,
                    PerWordStepLimit: saved.Limits.PerWordStepLimit);
            }
            else
            {
                selectionRequest = request.Selection;
            }

            onProgress?.Invoke(new AssessmentProgress(
                AssessmentStage.SelectingWords, 0, null, "Composing the Selection..."));
            SelectionComposition composition;
            // One copy of the Baseline serves both the Selection and the naming, and is released when the run ends.
            using var baselineReader = BaselineReadCache.Open(baseline.FwDataPath);
            {
                var cache = baselineReader.Cache;
                var composed = SelectionComposer.Compose(
                    cache, selectionRequest, assessments, JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions()));
                if (!composed.Succeeded)
                    return CommandOutcome<AssessCommandResponse>.Refused(composed.Refusal!);
                composition = composed.Value!;
                if (namedSelection is not null)
                    composition = composition with { Selection = composition.Selection with { Name = namedSelection } };
            }

            if (request.FullObjectTimingDetail && (request.Selection is null ||
                composition.Selection.Words.Count > AssessmentScopeConfiguration.DefaultTimingDetailWords))
                return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                    "assess.timing-detail-selection", FailureReason.InvalidArgument,
                    $"Full object timing requires an explicit Selection of at most {AssessmentScopeConfiguration.DefaultTimingDetailWords} words."));

            if (request.ReplaceAssessmentId is { } replacedId)
            {
                AssessmentRecord? replaced;
                try { replaced = assessments.Get(replacedId); }
                catch (KeyNotFoundException) { replaced = null; }
                var tokenJson = JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions());
                if (replaced is null || replaced.Kind != AssessmentKind.ParseTime.ToStoredKind() ||
                    replaced.ProposalId is not null || replaced.ReplacesAssessmentId is not null ||
                    replaced.BaselineToken != tokenJson ||
                    composition.Selection.Words.Any(word => !replaced.Selection.Words.Contains(word, StringComparer.Ordinal)))
                    return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                        "assess.invalid-replacement", FailureReason.InvalidArgument,
                        "Parse these words again requires a complete Assessment of this Baseline containing every requested word."));
            }

            AssessmentScope.Words scope;
            var exportedCandidate = Path.GetDirectoryName(baseline.FwDataPath)!;
            IReadOnlyList<ProducedAssessment> produced;
            try
            {
                var supportedKinds = assessor.SupportedKinds
                    .Where(kind => kind != AssessmentKind.PhonologyExamples).ToArray();
                IReadOnlyList<AssessmentKind> collected;
                if (configured.Collect.Count == 0)
                {
                    collected = supportedKinds.Contains(AssessmentKind.Correctness)
                        ? CollectedKinds.Append(AssessmentKind.Correctness).ToArray() : CollectedKinds;
                }
                else
                {
                    var unknown = configured.Collect.FirstOrDefault(name =>
                        !Enum.TryParse<AssessmentKind>(name, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed));
                    if (unknown is not null)
                        return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                            "assess.unsupported-kind", FailureReason.Refused,
                            $"The project's assessment scope asks to collect '{unknown}', which is not an Assessment kind.",
                            new Dictionary<string, string> { ["kind"] = unknown }));
                    // Assess always reports parse time, so the scope's list chooses what is collected beside it.
                    collected = configured.Collect
                        .Select(name => Enum.Parse<AssessmentKind>(name, ignoreCase: true))
                        .Prepend(AssessmentKind.ParseTime).Distinct().ToArray();
                }
                var unsupported = collected.Where(kind => !supportedKinds.Contains(kind)).ToArray();
                if (unsupported.Length > 0)
                    return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                        "assess.unsupported-kind", FailureReason.Refused,
                        $"The Assessor does not declare required Assessment kind '{unsupported[0]}'.",
                        new Dictionary<string, string> { ["kind"] = unsupported[0].ToString() }));
                var stepLimit = request.PerWordStepLimit ?? selectionRequest.PerWordStepLimit ??
                    defaultLimits?.PerWordStepLimit ?? configured.PerWordStepLimit;
                var policy = defaultLimits is null
                    ? SelectionLimitPolicy.Estimated(stepLimit)
                    : defaultLimits with { PerWordStepLimit = stepLimit };
                var limits = frozenLimits ?? (request.PerWordLimitMs is { } ms
                    ? new TrialLimits(TimeSpan.FromMilliseconds(ms), stepLimit)
                    : ParseRunLimits.Resolve(policy, ParseRunLimits.ReadRate(assessments)));
                scope = new AssessmentScope.Words(composition.Selection.Words, collected,
                    limits.PerWordLimit, limits.PerWordStepLimit);
                var timeLimit = limits.PerWordLimit;
                var latestWordProgress = new TrialWordProgress(0, scope.WordForms.Count, null);
                onProgress?.Invoke(new AssessmentProgress(
                    AssessmentStage.Parsing, 0, composition.Selection.Words.Count, "Parsing the Selection...")
                    { PerWordLimitMs = timeLimit is { } wordLimit ? (int)wordLimit.TotalMilliseconds : null });
                Action<bool>? onAdmissionWait = onProgress is null ? null : waiting =>
                {
                    var wordProgress = latestWordProgress;
                    onProgress(new AssessmentProgress(AssessmentStage.Parsing,
                        wordProgress.Completed, wordProgress.Total,
                        waiting ? "Waiting for parser capacity." : "Parser capacity wait ended.")
                    {
                        CurrentWord = wordProgress.CurrentWord,
                        PerWordLimitMs = scope.PerWordLimit is { } limit ? (int)limit.TotalMilliseconds : null,
                        StoppedWords = wordProgress.StoppedWords,
                        SlowestWord = wordProgress.SlowestWord,
                    });
                };
                produced = assessor.ProduceAsync(scope, exportedCandidate, wordProgress =>
                {
                    latestWordProgress = wordProgress;
                    onProgress?.Invoke(new AssessmentProgress(AssessmentStage.Parsing,
                        wordProgress.Completed, wordProgress.Total,
                        wordProgress.CurrentWord is { } word ? $"Parsing {word}" : "Reading word results...")
                    {
                        CurrentWord = wordProgress.CurrentWord,
                        PerWordLimitMs = scope.PerWordLimit is { } limit ? (int)limit.TotalMilliseconds : null,
                        StoppedWords = wordProgress.StoppedWords,
                        SlowestWord = wordProgress.SlowestWord,
                    });
                }, onAdmissionWait, cancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
            }
            catch (AssessorUnavailableException ex)
            {
                var refusal = ex.MemoryKill is { } memoryKill
                    ? ParserMemoryRefusal.From(request.ProjectPath, memoryKill)
                    : ex.ParserRefusal is { } failure
                        ? ParserExecutionRefusal.From(RefusalCodes.AssessParserUnavailable, request.ProjectPath, failure)
                        : ParserUnavailable(request.ProjectPath, ex.Message, ex.ExecutableMissing);
                new ParserRefusalRepository(database).Save(baseline.Token, refusal);
                return CommandOutcome<AssessCommandResponse>.Refused(refusal);
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
                var expectedKinds = (scope.Collect.Count == 0
                    ? assessor.SupportedKinds.Where(kind => kind != AssessmentKind.PhonologyExamples).ToArray()
                    : scope.Collect).ToArray();
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
                    new StoredScope.Trial(ScopeQuery, scope.WordForms, scope.Collect, scope.PerWordLimit, scope.PerWordStepLimit));
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
                    var record = AssessmentMaterial.ToWordRecord(item, assessmentId, proposalId: null,
                        proposalIntentDigest: null, assessor.Name, scopeJson, scopeDigest, baselineTokenJson,
                        new AssessmentSubject.Words(composition.Selection, TokeniserName, TokeniserVersion)) with
                    { SavedUtc = savedUtc, ReplacesAssessmentId = request.ReplaceAssessmentId };
                    pendingRecords.Add(record);
                    assessmentIds.Add(assessmentId);
                    if (record.RequireWordMaterial().CachePath is not null)
                    {
                        statsCachePath = record.RequireWordMaterial().CachePath;
                        statisticsRecord = record;
                    }
                }

                var invocations = pendingRecords.Select(record => record.RequireWordMaterial().Invocation).Distinct().ToArray();
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
                IReadOnlyDictionary<string, PanGlossWordStatistics>? wordStats = null;
                IReadOnlyList<AssessmentObjectTiming> objectTimings = [];
                AssessmentTimingEvidence? timingEvidence = null;
                if (statsCachePath is null)
                {
                    summaryMarkdown = "(no per-object statistics were collected)" + Environment.NewLine;
                    timingEvidence = new AssessmentTimingEvidence([], 0, false, 0) { StatisticsUnavailable = true };
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
                    BoundedPanGlossStatistics batchStatistics;
                    try
                    {
                        batchStatistics = PanGlossBatchStatisticsReader.ReadBounded(
                            replay.CachePath, composition.Selection.Words,
                            request.FullObjectTimingDetail ? composition.Selection.Words.Count :
                                ObjectTimingRowCapForTests.Value ?? configured.TimingDetailWords);
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or
                        UnauthorizedAccessException or SqliteException)
                    {
                        return CommandOutcome<AssessCommandResponse>.Refused(
                            ParserUnavailable(request.ProjectPath, exception.Message));
                    }
                    wordStats = batchStatistics.Words.ToDictionary(pair => pair.Key, pair => pair.Value,
                        StringComparer.Ordinal);
                    objectTimings = batchStatistics.ObjectTimings.Select(row => new AssessmentObjectTiming(
                        row.Kind, row.Key, row.IdentityQuality, row.Direction, row.Object, row.Word,
                        row.Attempts, row.Passes, row.ElapsedNs)).ToArray();
                    timingEvidence = new AssessmentTimingEvidence(batchStatistics.Aggregates,
                        batchStatistics.DetailWordCount, batchStatistics.DetailComplete, batchStatistics.TotalObjectTimingRows)
                    {
                        AttributedNs = batchStatistics.AttributedNs, UnattributedNs = batchStatistics.UnattributedNs,
                        OverrunNs = batchStatistics.OverrunNs,
                    };
                    summaryMarkdown = $"Batch statistics: {batchStatistics.Words.Count} words; " +
                        $"{batchStatistics.TotalObjectTimingRows} object timing rows.";
                    if (!batchStatistics.DetailComplete)
                        summaryMarkdown += $" Object timing detail kept for the {batchStatistics.DetailWordCount} costliest words.";
                }

                if (cancellationToken.IsCancellationRequested)
                    return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
                var batchWords = produced.Select(item => item.Raw).OfType<AssessmentRaw.Batch>()
                    .SelectMany(item => item.Analysis.Words).ToArray();
                wordStats = BatchAnalysis.OverlayRetryStatistics(wordStats, batchWords);
                if (timingEvidence is { StatisticsUnavailable: false } && batchWords.Any(word => word.WasRetried))
                {
                    timingEvidence = timingEvidence with { DetailComplete = false, HasRetriedWords = true };
                    summaryMarkdown += " Object timing describes the initial pass; some words were retried.";
                }
                var namingCache = baselineReader.Cache;
                var projectName = Path.GetFileNameWithoutExtension(request.ProjectPath);
                var wordContext = ReadProjectWordContext(namingCache, composition.Selection.Words,
                    composition.Descriptor.TextIds);
                var navigation = SavedProjectNavigation.Read(project.FullFwDataPath, baseline.Token.ProjectIdentity);
                var storedContext = BaselineWordContext.Read(namingCache, navigation, composition.Selection.Words,
                    wordContext.Approved, wordContext.Rejected, wordContext.Candidates);
                // Named once and recorded, so a later read of the stored words glosses them as this run does.
                var namedMissed = new Dictionary<string, ParserReading[]?>(StringComparer.Ordinal);
                ParserReading[]? NameMissed(string word, WordCorrectness? correctness)
                {
                    if (namedMissed.TryGetValue(word, out var named)) return named;
                    return namedMissed[word] = correctness?.Unmatched
                        .Select(index => correctness.Expectations[index])
                        .Select(missed => ReadStoredAnalysis(namingCache, projectName, missed, ReadingGrade.Approved, navigation.LinkFor))
                        .ToArray();
                }
                var parseWordCount = pendingRecords
                    .FirstOrDefault(record => record.Kind == AssessmentKind.ParseTime.ToStoredKind())?.Words.Count ?? 0;
                var finalizing = new FinalizingProgress(onProgress, parseWordCount);
                finalizing.Report(0);
                // Grades and comparison facts depend only on these inputs, so every record shares one computation.
                var gradeMemo = new Dictionary<(string, IReadOnlyList<ParseAnalysis>), IReadOnlyList<string>>();
                var comparisonMemo = new Dictionary<
                    (string, ParseWordEvidence?, string, bool, WordCorrectness?), WordAnalysisComparison?>();
                IReadOnlyList<string>? GradesOf(string wordText, ParseWordEvidence? morphology)
                {
                    if (morphology is null) return null;
                    var key = (wordText, morphology.Analyses);
                    if (gradeMemo.TryGetValue(key, out var cached)) return cached;
                    return gradeMemo[key] = GradeReadings(morphology.Analyses,
                        wordContext.Approved.GetValueOrDefault(wordText) ?? Array.Empty<ApprovedMorphology>(),
                        wordContext.Rejected.GetValueOrDefault(wordText) ?? Array.Empty<ApprovedMorphology>(),
                        wordContext.Candidates.GetValueOrDefault(wordText) ?? Array.Empty<ApprovedMorphology>());
                }
                WordAnalysisComparison? ComparisonOf(AssessedWord word)
                {
                    var key = (word.Word, word.Morphology, word.Outcome, word.IsIncomplete, word.Correctness);
                    if (comparisonMemo.TryGetValue(key, out var cached)) return cached;
                    finalizing.FirstPassWordDone();
                    return comparisonMemo[key] = CompareSemantics.Capture(new CompareWordFacts(
                        wordContext.Standings.GetValueOrDefault(word.Word), word.Outcome, word.IsIncomplete,
                        word.Morphology, null, word.Correctness?.Unmatched.Count ?? 0)
                    {
                        StoredAnalyses = storedContext.Analyses.GetValueOrDefault(word.Word) ?? [],
                        StoredAnalysesAvailable = true,
                        MissedApproved = NameMissed(word.Word, word.Correctness),
                    });
                }
                try
                {
                    pendingRecords = pendingRecords.Select(record => record with
                    {
                        Material = record.RequireWordMaterial() with
                        {
                        Rows = record.RequireWordMaterial().Rows.Select(word =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var stats = wordStats is not null && wordStats.TryGetValue(word.Word, out var found)
                                ? found : null;
                            return word with
                            {
                                ElapsedNs = stats?.ElapsedNs,
                                Attempts = stats?.Attempts ?? word.Attempts,
                                Passes = stats?.Passes ?? word.Passes,
                                WorkSteps = stats?.WorkSteps ?? word.WorkSteps,
                                Morphology = word.Morphology is { } morphology && stats is not null
                                    ? morphology with { Attempts = stats.Attempts }
                                    : word.Morphology,
                                ProjectStanding = wordContext.Standings.GetValueOrDefault(word.Word),
                                OccurrenceCount = wordContext.HasTextSelection
                                    ? wordContext.OccurrencesByWord.GetValueOrDefault(word.Word) : null,
                                ReadingGrades = GradesOf(word.Word, word.Morphology),
                                AnalysisComparison = ComparisonOf(word),
                                MissedApprovedCount = word.Correctness?.Unmatched.Count,
                                MissedApproved = NameMissed(word.Word, word.Correctness),
                            };
                        }).ToArray(),
                        ObjectTimings = record.Kind == AssessmentKind.ParseTime.ToStoredKind()
                            ? objectTimings : record.RequireWordMaterial().ObjectTimings,
                        TimingEvidence = record.Kind == AssessmentKind.ParseTime.ToStoredKind() ? timingEvidence : null,
                        },
                    }).ToList();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
                }
                var currentBaseline = baselines.GetCurrentPublished(workspaceKey);
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
                var comparisonByWord = pendingRecords.FirstOrDefault(record => record.Kind == AssessmentKind.ParseTime.ToStoredKind())
                    ?.Words.ToDictionary(word => word.Word, word => word.AnalysisComparison, StringComparer.Ordinal);
                var words = pendingRecords.FirstOrDefault(record => record.Kind == AssessmentKind.ParseTime.ToStoredKind())
                    ?.Words.Select(AssessmentWordRows.FromRecorded).ToArray() ?? Array.Empty<AssessmentWordResult>();
                var completionSummary = AssessmentWordRows.CompletionSummary(words);
                summaryMarkdown = RenderSummaryMarkdown(completionSummary, summaryMarkdown);
                var grammarWarnings = invocation.GrammarWarningLines is { Count: > 0 } warningLines
                    ? warningLines : null;
                if (words.Length > 0)
                {
                    var origin = new WordMeasurementOrigin(
                        pendingRecords.Single(record => record.Kind == AssessmentKind.ParseTime.ToStoredKind()).AssessmentId,
                        invocation.InvocationId,
                        DateTimeOffset.Parse(savedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
                    try
                    {
                        words = words.Select(word =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            finalizing.SecondPassWordDone();
                            var readings = word.Morphology is null
                                ? null : ParserReadingReader.Read(namingCache, projectName, word.Morphology, navigation.LinkFor);
                            var readingGrades = GradesOf(word.Word, word.Morphology);
                            var projectStanding = wordContext.Standings.GetValueOrDefault(word.Word);
                            var missedApproved = NameMissed(word.Word, word.Correctness);
                            var storedAnalyses = storedContext.Analyses[word.Word];
                            var stats = wordStats is not null && wordStats.TryGetValue(word.Word, out var found) ? found : null;
                            var row = word with
                            {
                                Origin = origin,
                                Readings = readings,
                                TryWordLink = storedContext.WordLinks.GetValueOrDefault(word.Word),
                                WordWritingSystem = storedContext.WordWritingSystems.GetValueOrDefault(word.Word),
                                ReadingGrades = readingGrades,
                                ProjectStanding = projectStanding,
                                OccurrenceCount = wordContext.HasTextSelection
                                    ? wordContext.OccurrencesByWord.GetValueOrDefault(word.Word) : null,
                                MissedApproved = missedApproved,
                                ExpectedAnalysis = AssessmentWordRows.ExpectedAnalysis(storedAnalyses),
                                StoredAnalyses = storedAnalyses,
                                StoredAnalysesAvailable = true,
                                AnalysisComparison = comparisonByWord?.GetValueOrDefault(word.Word),
                                Attempts = stats?.Attempts ?? word.Attempts,
                                Passes = stats?.Passes ?? word.Passes,
                                WorkSteps = stats?.WorkSteps ?? word.WorkSteps,
                            };
                            return row with { FixFirst = AssessmentWordRows.FixFirst(row), Comparison = CompareSemantics.Compare(row) };
                        }).ToArray();
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
                    }
                }

                var response = new AssessCommandResponse(baseline, composition.Projection, assessmentIds, summaryMarkdown)
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
                        record.RequireWordMaterial().Invocation?.InvocationId ?? throw new InvalidDataException(
                            "An Assessment is missing its invocation evidence."))).ToArray(),
                    InvocationId = invocation.InvocationId,
                    SelectionDescriptor = composition.Descriptor,
                };
                onProgress?.Invoke(new AssessmentProgress(
                    AssessmentStage.Complete, assessmentIds.Count, assessmentIds.Count, completionSummary));
                if (cancellationToken.IsCancellationRequested)
                    return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
                try
                {
                    retainedInvocations.Record(retained, pendingRecords, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
                }
                // Retained before any housekeeping so a later failure cannot delete artifacts the saved Assessment uses.
                foreach (var lease in artifactLeases) lease.Retain();
                try
                {
                    ClearParserRefusal(database, request.ProjectPath, baseline.Token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The Assessment is saved and the next successful Assess clears the stale refusal again.
                    ProjectDiagnosticLog.Append(request.ProjectPath,
                        "Assess saved its Assessment but could not clear the stale parser refusal.", exception);
                }
                return CommandOutcome<AssessCommandResponse>.Success(response);
            }
            finally
            {
                foreach (var lease in artifactLeases) lease.Dispose();
            }
        });
    }

    // A test narrows the timings cap for its own async flow without touching any other running test.
    internal static readonly AsyncLocal<int?> ObjectTimingRowCapForTests = new();

    private sealed class FinalizingProgress(Action<AssessmentProgress>? onProgress, int total)
    {
        private int _firstPass;
        private int _secondPass;
        private int _lastReported = -1;
        private readonly int _step = Math.Max(1, Math.Min(500, total / 100));

        public void FirstPassWordDone() { _firstPass++; Report(); }

        public void SecondPassWordDone() { _secondPass++; Report(); }

        private void Report()
        {
            // The two passes each cover the Selection once, so their average is the share of words finished.
            var completed = Math.Min(total, (_firstPass + _secondPass) / 2);
            if (completed - _lastReported >= _step) Report(completed);
        }

        public void Report(int completed)
        {
            _lastReported = completed;
            onProgress?.Invoke(new AssessmentProgress(AssessmentStage.Finalizing, completed, total,
                $"Preparing results: {completed:N0} of {total:N0} words"));
        }
    }

    private static void ClearParserRefusal(MotifDatabase database, string projectPath, SIL.Motif.Contract.Baselines.BaselineToken token)
    {
        var requested = Environment.GetEnvironmentVariable("MOTIF_TEST_BUSY_PARSER_REFUSAL_CLEAR_FOR");
        if (requested is not null && string.Equals(Path.GetFullPath(requested), Path.GetFullPath(projectPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new Microsoft.Data.Sqlite.SqliteException("database is locked", 5);
        new ParserRefusalRepository(database).Clear(token);
    }

    // No Baseline yet: capture and publish one. One already current: reuse it rather than recapturing.
    private static CommandOutcome<BaselineCaptureResponse> EnsureBaseline(string projectPath, string managedRoot,
        ProjectLocator project, string workspaceKey, BaselineRepository baselines)
    {
        var current = baselines.GetCurrentPublished(workspaceKey);
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

    private static Refusal Cancelled(string projectPath) => new(
        "assessment.cancelled", FailureReason.Cancelled,
        "The Assessment run was cancelled; no Assessments were recorded.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["projectPath"] = projectPath });

    private static ProjectWordContext ReadProjectWordContext(
        LcmCache cache, IReadOnlyList<string> words, IReadOnlyList<Guid> textIds)
    {
        // Only the selected words: a lexicon-wide read builds every analysis in the project.
        var forms = ApprovedMorphologyReader.FormsOf(words);
        var approved = ApprovedMorphologyReader.Read(cache, forms);
        var rejected = ApprovedMorphologyReader.ReadDisapproved(cache, forms);
        var candidates = ApprovedMorphologyReader.ReadCandidates(cache, forms);
        var misspelled = ApprovedMorphologyReader.ReadIncorrectSpellings(cache, forms);
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

    internal static ParserReading ReadStoredAnalysis(
        LcmCache cache, string projectName, ApprovedMorphology analysis, string opinion,
        Func<FieldWorksLinkTarget?, string?>? liveLink = null)
    {
        var morphs = analysis.Morphs.Select(morph =>
            new ParseMorph(morph.Form, morph.Msa, morph.InflType, GuessedString: null)).ToArray();
        return new ParserReading(ParserReadingReader.ReadMorphs(cache, projectName, morphs, liveLink))
        {
            StoredAnalysisId = analysis.SourceAnalysisId,
            StoredAnalysisOpinion = opinion,
            Identity = analysis,
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
/// <see cref="SupportedKinds"/> or call <see cref="ProduceAsync(AssessmentScope, string, CancellationToken)"/> only after that validation. A discovery
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

    public Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
        AssessmentScope scope, string exportedCandidate, Action<TrialWordProgress> onProgress,
        CancellationToken cancellationToken) =>
        Resolve().ProduceAsync(scope, exportedCandidate, onProgress, cancellationToken);

    public Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
        AssessmentScope scope, string exportedCandidate, Action<TrialWordProgress> onProgress,
        Action<bool>? onAdmissionWait, CancellationToken cancellationToken) =>
        Resolve().ProduceAsync(scope, exportedCandidate, onProgress, onAdmissionWait, cancellationToken);

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
