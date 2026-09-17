using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Assess;

/// <summary>
/// Measures the current Baseline synchronously (design decision 4): ensures a Baseline exists, composes a
/// Selection from whichever of the four agreed sources were asked for, runs the Assessor over it through the
/// PanGloss invocation for batch and statistics requests, records every produced Assessment,
/// and renders PanGloss's default statistics view as a text summary.
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
    /// Measures the project under an explicitly supplied managed root. The single-argument overload is what
    /// production code and the CLI call; this one exists so a test can supply its own disposable root.
    /// </summary>
    public static CommandOutcome<AssessCommandResponse> Assess(
        AssessRequest request, string managedRoot, Action<AssessmentProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var ownership = WorkspaceOwnership.Bootstrap(managedRoot);
        using var invoker = new PanGlossInvoker();
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
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var baselines = new BaselineRepository(database);
            var assessments = new AssessmentRepository(database);
            var retainedInvocations = new RetainedInvocationRepository(database);

            onProgress?.Invoke(new AssessmentProgress(
                AssessmentStage.Capturing, 0, null, "Ensuring a current Baseline exists..."));
            var baselineOutcome = EnsureBaseline(request.ProjectPath, managedRoot, project, workspaceKey, baselines);
            if (!baselineOutcome.Succeeded)
                return CommandOutcome<AssessCommandResponse>.Refused(baselineOutcome.Refusal!);
            var baseline = baselineOutcome.Value!;

            onProgress?.Invoke(new AssessmentProgress(
                AssessmentStage.SelectingWords, 0, null, "Composing the Selection..."));
            SelectionComposition composition;
            using (var cache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath))
            {
                var composed = SelectionComposer.Compose(
                    cache, request.Selection, assessments, JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions()));
                if (!composed.Succeeded)
                    return CommandOutcome<AssessCommandResponse>.Refused(composed.Refusal!);
                composition = composed.Value!;
            }

            AssessmentScope scope;
            var exportedCandidate = Path.GetDirectoryName(baseline.FwDataPath)!;

            onProgress?.Invoke(new AssessmentProgress(
                AssessmentStage.Parsing, 0, composition.Selection.Words.Count, "Parsing the Selection..."));
            IReadOnlyList<ProducedAssessment> produced;
            try
            {
                var collected = assessor.SupportedKinds.Contains(AssessmentKind.Correctness)
                    ? CollectedKinds.Append(AssessmentKind.Correctness).ToArray() : CollectedKinds;
                var unsupported = collected.Where(kind => !assessor.SupportedKinds.Contains(kind)).ToArray();
                if (unsupported.Length > 0)
                    return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                        "assess.unsupported-kind", FailureReason.Refused,
                        $"The Assessor does not declare required Assessment kind '{unsupported[0]}'.",
                        new Dictionary<string, string> { ["kind"] = unsupported[0].ToString() }));
                scope = new AssessmentScope(composition.Selection.Words, collected,
                    AssessmentScopeConfiguration.DefaultPerWordLimit);
                produced = assessor.ProduceAsync(scope, exportedCandidate, cancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
            }
            catch (AssessorUnavailableException ex)
            {
                return CommandOutcome<AssessCommandResponse>.Refused(ParserUnavailable(request.ProjectPath, ex.Message));
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
                if (invocations.Any(item => item is null) ||
                    invocations.OfType<BatchInvocationEvidence>().Distinct().Count() != 1 ||
                    pendingRecords.Any(record => record.GrammarSourceSha256 !=
                        invocations.OfType<BatchInvocationEvidence>().Single().SourceBytesSha256))
                {
                    return CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
                        "assess.invocation-inconsistent", FailureReason.Refused,
                        "The collected Assessments do not share one non-null invocation evidence record " +
                        "and its source-byte digest."));
                }
                var invocation = invocations.OfType<BatchInvocationEvidence>().Single();

                onProgress?.Invoke(new AssessmentProgress(
                    AssessmentStage.ReadingStatistics, 0, null, "Reading PanGloss's statistics..."));
                string summaryMarkdown;
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
                    var summary = invoker.RunAsync(
                            new PanGlossRequest.Stats(replay.GrammarPath, replay.CachePath, Array.Empty<string>()),
                            "assess:stats:" + workspaceKey, cancellationToken)
                        .GetAwaiter().GetResult();
                    switch (summary)
                    {
                        case PanGlossOutcome.Completed completed:
                            summaryMarkdown = "```" + Environment.NewLine + completed.Output + "```" + Environment.NewLine;
                            break;
                        case PanGlossOutcome.Cancelled:
                            return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
                        default:
                            return CommandOutcome<AssessCommandResponse>.Refused(
                                ParserUnavailable(request.ProjectPath, summary.Message));
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                    return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
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
                    ? batch.Analysis.Words.Select(word => new AssessmentWordResult(
                        word.Word, word.Outcome.ToStoredOutcome(),
                        word.Outcome is WordOutcome.Capped or WordOutcome.TimedOut,
                        word.Outcome switch
                        {
                            _ when word.Morphology is { Capped: true, TimedOut: true } =>
                                "INCOMPLETE — parsing did not finish (step and time limits)",
                            WordOutcome.Capped => "INCOMPLETE — parsing did not finish (step limit)",
                            WordOutcome.TimedOut => "INCOMPLETE — parsing did not finish (time limit)",
                            WordOutcome.Skipped => "Not attempted",
                            _ => "Search completed",
                        }, word.ElapsedMs, word.Signature)
                    { Morphology = word.Morphology, Correctness = word.Correctness }).ToArray()
                    : Array.Empty<AssessmentWordResult>();
                var completedCount = words.Count(word => !word.IsIncomplete && word.Outcome != "skipped");
                var searchNoun = completedCount == 1 ? "search" : "searches";
                var completionSummary = $"{completedCount} {searchNoun} completed; " +
                    $"{words.Count(word => word.IsIncomplete)} incomplete; {words.Count(word => word.Outcome == "skipped")} skipped.";
                summaryMarkdown = completionSummary + Environment.NewLine + Environment.NewLine + summaryMarkdown;
                var grammarWarnings = invocation?.GrammarWarningLines is { Count: > 0 } warningLines
                    ? warningLines : null;

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
                            record.AssessmentId, record.Kind, record.Invocation!.InvocationId)).ToArray(),
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

    private static Refusal ParserUnavailable(string projectPath, string message) => new(
        "assess.parser-unavailable", FailureReason.Refused, message,
        new Dictionary<string, string>(StringComparer.Ordinal) { ["projectPath"] = projectPath });

    private static Refusal Cancelled(string projectPath) => new(
        "assessment.cancelled", FailureReason.Cancelled,
        "The Assessment run was cancelled; no Assessments were recorded.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["projectPath"] = projectPath });

    private static string ResolveProductVersion() => MotifProductVersion.CurrentText;
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
            throw new AssessorUnavailableException(Name, exception.Message);
        }
    }
}
