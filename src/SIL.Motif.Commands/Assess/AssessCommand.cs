using System;
using System.Collections.Generic;
using System.IO;
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
using SIL.Motif.Worker.PanGloss;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Assess;

/// <summary>
/// Measures the current Baseline synchronously (design decision 4): ensures a Baseline exists, composes a
/// Selection from whichever of the four agreed sources were asked for, runs the Assessor over it under the
/// machine-wide PanGloss admission queue, records every produced Assessment, and renders PanGloss's default
/// statistics view as a text summary.
/// </summary>
/// <remarks>
/// <para>
/// This command never wakes the durable job runner — it captures, measures, and returns within one call,
/// the same synchronous shape <see cref="BaselineCaptureCommand"/> already established. A cancelled run
/// records nothing: <see cref="Run"/> only calls <see cref="SIL.Motif.Worker.Store.AssessmentRepository.Record"/>
/// after the Assessor has already returned, so a cancellation raised while it is still running never leaves
/// a partial Assessment behind.
/// </para>
/// <para>
/// Shares its interpretation of <c>ProducedAssessment</c> with <c>TrialJobHandler</c> through
/// <see cref="AssessmentMaterial"/> rather than forking it: two independent readings of the same raw shape
/// could silently drift apart.
/// </para>
/// </remarks>
public static class AssessCommand
{
    // Reaches PanGloss's `--engine=default` (hc-rust) rather than the fast default (design decision 4).
    private const string EngineName = "accurate";

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
        using var queue = new MachinePanGlossQueue();
        return Run(request, managedRoot, () => new PanGlossAssessor(new StatsCacheStore(ownership)),
            () => new PanGlossStatsQueryProcess(), queue, onProgress, cancellationToken);
    }

    /// <summary>
    /// Measures the project against explicitly supplied collaborators — a fake Assessor and statistics
    /// query stand in for a real PanGloss subprocess in tests, and a custom-slotted <see cref="MachinePanGlossQueue"/>
    /// keeps a test's admission race off the machine's real, well-known slots.
    /// </summary>
    /// <remarks>
    /// The collaborators arrive as factories rather than instances because building a real one locates the
    /// <c>pangloss</c> executable and throws when it is absent. Constructed eagerly, that throw would beat
    /// this command's own project check, so a mistyped path would report a missing parser instead of a
    /// missing project. They are built inside the store callback, after the project has resolved.
    /// </remarks>
    internal static CommandOutcome<AssessCommandResponse> Run(
        AssessRequest request, string managedRoot, Func<IAssessor> assessorFactory,
        Func<IPanGlossStatsQuery> statsQueryFactory, MachinePanGlossQueue queue,
        Action<AssessmentProgress>? onProgress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assessorFactory);
        ArgumentNullException.ThrowIfNull(statsQueryFactory);
        ArgumentNullException.ThrowIfNull(queue);

        return ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, project) =>
        {
            IAssessor assessor;
            IPanGlossStatsQuery statsQuery;
            try
            {
                assessor = assessorFactory();
                statsQuery = statsQueryFactory();
            }
            catch (ParserUnavailableException ex)
            {
                return CommandOutcome<AssessCommandResponse>.Refused(ParserUnavailable(request.ProjectPath, ex));
            }

            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var baselines = new BaselineRepository(database);
            var assessments = new AssessmentRepository(database);

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
                var composed = SelectionComposer.Compose(cache, request.Selection, assessments);
                if (!composed.Succeeded)
                    return CommandOutcome<AssessCommandResponse>.Refused(composed.Refusal!);
                composition = composed.Value!;
            }

            var scope = new AssessmentScope(composition.Selection.Words, EngineName, CollectedKinds,
                AssessmentScopeConfiguration.DefaultPerWordLimit);
            var exportedCandidate = Path.GetDirectoryName(baseline.FwDataPath)!;

            onProgress?.Invoke(new AssessmentProgress(
                AssessmentStage.Parsing, 0, composition.Selection.Words.Count, "Parsing the Selection..."));
            IReadOnlyList<ProducedAssessment> produced;
            try
            {
                produced = queue.RunAsync(
                    "assess:" + workspaceKey,
                    (cpuJob, jobToken) => assessor.ProduceAsync(
                        scope, exportedCandidate, jobToken, new WindowsCpuJobGovernor(cpuJob)),
                    cancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
            }
            catch (ParserUnavailableException ex)
            {
                return CommandOutcome<AssessCommandResponse>.Refused(ParserUnavailable(request.ProjectPath, ex));
            }

            var scopeJson = ScopeCodec.Write(
                new StoredScope.Trial(ScopeQuery, scope.Words, scope.Engine, scope.Collect, scope.PerWordLimit));
            var scopeDigest = AssessmentMaterial.Digest(scopeJson);
            var baselineTokenJson = JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions());

            var assessmentIds = new List<string>();
            string? statsCachePath = null;
            foreach (var item in produced)
            {
                var assessmentId = CanonicalId.Mint("assessment/").Value;
                var record = AssessmentMaterial.ToRecord(item, assessmentId, proposalId: null,
                    proposalIntentDigest: null, assessor.Name, scopeJson, scopeDigest, TokeniserName,
                    TokeniserVersion, baselineTokenJson, composition.Selection);
                assessments.Record(record);
                assessmentIds.Add(assessmentId);
                if (record.CachePath is not null) statsCachePath = record.CachePath;
            }

            onProgress?.Invoke(new AssessmentProgress(
                AssessmentStage.ReadingStatistics, 0, null, "Reading PanGloss's statistics..."));
            var summaryMarkdown = statsCachePath is null
                ? RenderSummary(statsQuery, baseline.FwDataPath, null, null, cancellationToken)
                : queue.RunAsync(
                    "assess:stats:" + workspaceKey,
                    (cpuJob, jobToken) => Task.FromResult(RenderSummary(
                        statsQuery, baseline.FwDataPath, statsCachePath, new WindowsCpuJobGovernor(cpuJob), jobToken)),
                    cancellationToken).GetAwaiter().GetResult();

            onProgress?.Invoke(new AssessmentProgress(
                AssessmentStage.Complete, assessmentIds.Count, assessmentIds.Count, "Assessment complete."));
            return CommandOutcome<AssessCommandResponse>.Success(
                new AssessCommandResponse(baseline, composition.Projection, assessmentIds, summaryMarkdown));
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

    // The Assessor's own per-object cache is the only source for anything beyond parse coverage.
    private static string RenderSummary(
        IPanGlossStatsQuery statsQuery, string grammarPath, string? cachePath,
        WindowsCpuJobGovernor? governor, CancellationToken cancellationToken)
    {
        if (cachePath is null) return "(no per-object statistics were collected)" + Environment.NewLine;

        var output = statsQuery.QueryAsync(grammarPath, cachePath, Array.Empty<string>(), cancellationToken, governor)
            .GetAwaiter().GetResult();
        return "```" + Environment.NewLine + output.StandardOutput + "```" + Environment.NewLine;
    }

    private static Refusal ParserUnavailable(string projectPath, ParserUnavailableException ex) => new(
        "assess.parser-unavailable", FailureReason.Refused, ex.Message,
        new Dictionary<string, string>(StringComparer.Ordinal) { ["projectPath"] = projectPath });

    private static Refusal Cancelled(string projectPath) => new(
        "assessment.cancelled", FailureReason.Refused,
        "The Assessment run was cancelled; no Assessments were recorded.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["projectPath"] = projectPath });

    private static string ResolveProductVersion() =>
        typeof(AssessCommand).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}
