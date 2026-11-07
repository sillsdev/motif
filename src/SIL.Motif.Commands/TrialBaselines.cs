using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// Records the Baseline side of a Trial: it assesses the Trial's words on the project as it stands, before the
/// Proposal is tried, so the Trial can later be compared with the human decisions FieldWorks holds.
/// </summary>
public static class TrialBaselines
{
    /// <summary>Assesses the words, or the project's default Selection when none are named.</summary>
    public static CommandOutcome<AssessCommandResponse> Assess(string projectPath, IReadOnlyList<string> words,
        string managedRoot, string? parserPath, CancellationToken cancellationToken) =>
        Assess(projectPath, words, managedRoot, parserPath, cancellationToken, null);

    /// <summary>Measures the before words under the deterministic limits chosen for their Trial.</summary>
    internal static CommandOutcome<AssessCommandResponse> Assess(string projectPath, IReadOnlyList<string> words,
        string managedRoot, string? parserPath, CancellationToken cancellationToken, TrialLimits? limits,
        ProjectParseLease? admission = null, string? writingSystemId = null,
        Action<IReadOnlyList<ParseWordEvidence>>? onPartial = null) =>
        AssessCommand.AssessWithLimits(new AssessRequest(projectPath,
            words.Count == 0 ? null : new SelectionRequest(false, [], words, false, null)),
            managedRoot, parserPath ?? PanGlossExecutable.TryLocate(), null, cancellationToken, limits, recordBudgetStop: false,
            admission: admission, reuseBefore: true, writingSystemId: writingSystemId, onPartial: onPartial);

    internal static CommandOutcome<T> WithAdmission<T>(EnqueueTrialRequest request,
        Func<ProjectParseLease?, Refusal?, CommandOutcome<T>> enqueue) where T : class?
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BaselineAssessmentIds is not null) return enqueue(null, null);
        return ProjectStoreCommand.Run(request.FwDataPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            using var admission = ProjectParseLease.TryAcquire(database);
            return enqueue(admission, admission is null ? ProjectParseLease.BusyRefusal() : null);
        });
    }

    /// <summary>
    /// Queues a Trial that carries its Baseline Assessments, so <c>difference produce</c> has a before side.
    /// Words are resolved the way <see cref="JobCommands.EnqueueTrial"/> resolves them, then assessed first.
    /// </summary>
    /// <param name="request">The Trial to queue.</param>
    /// <param name="managedRoot">The root holding the project's managed workspace.</param>
    /// <param name="parserPath">The parser to assess with, or <see langword="null"/> to locate one.</param>
    /// <param name="cancellationToken">Cancels the Baseline measurement.</param>
    /// <param name="onBaselineUnavailable">
    /// When supplied, a Baseline that cannot be assessed is reported to it and the Trial is queued without one;
    /// when <see langword="null"/>, the refusal ends the command.
    /// </param>
    public static CommandOutcome<JobEnqueuedResponse> EnqueueWithBaseline(EnqueueTrialRequest request,
        string managedRoot, string? parserPath, CancellationToken cancellationToken,
        Action<string>? onBaselineUnavailable = null) =>
        WithAdmission(request, (admission, busy) =>
        {
            var resolved = JobCommands.ResolveTrialWords(request);
            if (!resolved.Succeeded) return CommandOutcome<JobEnqueuedResponse>.Refused(resolved.Refusal!);
            if (resolved.Value is null && !request.AllWords && onBaselineUnavailable is null && RequireDefault(request) is { } missing)
                return CommandOutcome<JobEnqueuedResponse>.Refused(missing);
            if (busy is null && request.BaselineAssessmentIds is null)
            {
                var baseline = EnsureCurrentBaseline(request, managedRoot);
                if (!baseline.Succeeded && onBaselineUnavailable is null)
                    return CommandOutcome<JobEnqueuedResponse>.Refused(baseline.Refusal!);
            }
            return EnqueueWithBaseline(request,
                (words, limits, writingSystem, onPartial) => busy is null
                    ? Assess(request.FwDataPath, words, managedRoot, parserPath, cancellationToken, limits, admission, writingSystem, onPartial)
                    : CommandOutcome<AssessCommandResponse>.Refused(busy),
                onBaselineUnavailable, parserPath ?? PanGlossExecutable.TryLocate(), admission);
        });

    internal static CommandOutcome<BaselineCaptureResponse> EnsureCurrentBaseline(EnqueueTrialRequest request, string managedRoot) =>
        ProjectStoreCommand.Run(request.FwDataPath, MotifProductVersion.CurrentText, (database, project) =>
            AssessCommand.EnsureBaseline(request.FwDataPath, managedRoot, project,
                ProjectWorkspaceKey.Compute(project), new SIL.Motif.Worker.Baselines.BaselineRepository(database)));

    internal static Refusal? RequireDefault(EnqueueTrialRequest request)
    {
        var selection = ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
            new NamedSelectionRepository(database).GetDefault() is null
                ? CommandOutcome<SelectionPresent>.Refused(new Refusal("selection.default-missing", FailureReason.Refused,
                    "No default Selection is saved for this project. Save one with `motif selection set-default` first."))
                : CommandOutcome<SelectionPresent>.Success(new SelectionPresent()));
        return selection.Refusal;
    }

    /// <summary>As the other overload, with the Baseline measurement supplied by the caller.</summary>
    internal static CommandOutcome<JobEnqueuedResponse> EnqueueWithBaseline(EnqueueTrialRequest request,
        Func<IReadOnlyList<string>, CommandOutcome<AssessCommandResponse>> assess,
        Action<string>? onBaselineUnavailable = null, string? expectedParserPath = null) =>
        EnqueueWithBaseline(request, (words, _) => assess(words), onBaselineUnavailable, expectedParserPath);

    /// <summary>Passes one frozen limit policy to the before measurement and the queued Trial.</summary>
    internal static CommandOutcome<JobEnqueuedResponse> EnqueueWithBaseline(EnqueueTrialRequest request,
        Func<IReadOnlyList<string>, TrialLimits?, CommandOutcome<AssessCommandResponse>> assess,
        Action<string>? onBaselineUnavailable = null, string? expectedParserPath = null, ProjectParseLease? admission = null) =>
        EnqueueWithBaseline(request, (words, limits, _, _) => assess(words, limits), onBaselineUnavailable, expectedParserPath, admission);

    internal static CommandOutcome<JobEnqueuedResponse> EnqueueWithBaseline(EnqueueTrialRequest request,
        Func<IReadOnlyList<string>, TrialLimits?, string?, Action<IReadOnlyList<ParseWordEvidence>>?, CommandOutcome<AssessCommandResponse>> assess,
        Action<string>? onBaselineUnavailable = null, string? expectedParserPath = null, ProjectParseLease? admission = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BaselineAssessmentIds is not null) return JobCommands.EnqueueTrialAdmitted(request, expectedParserPath, admission);
        var resolved = JobCommands.ResolveTrialWords(request);
        if (!resolved.Succeeded) return CommandOutcome<JobEnqueuedResponse>.Refused(resolved.Refusal!);
        var words = resolved.Value;
        var frozen = JobCommands.FreezeTrialLimits(request);
        if (!frozen.Succeeded) return CommandOutcome<JobEnqueuedResponse>.Refused(frozen.Refusal!);
        request = frozen.Value!;
        var captured = ReviewTrialCapture.Capture(request, words, expectedParserPath);
        if (!captured.Succeeded) return CommandOutcome<JobEnqueuedResponse>.Refused(captured.Refusal!);
        request = captured.Value!;
        if (request.ReviewInputs is not null) words = request.Words;
        var partitions = request.ReviewSnapshot is { } review
            ? ReviewInputPartitions.Create(review) : [new ReviewParsePartition(null, words ?? [], [])];
        if (partitions.Count == 0) partitions = [new ReviewParsePartition(null, words ?? [], [])];
        var assessmentIds = new List<string>();
        var baselines = new List<ReviewBaselinePartition>();
        var partialRows = new List<TrialReviewObservation>();
        foreach (var partition in partitions)
        {
            CommandOutcome<AssessCommandResponse> before;
            try { before = assess(partition.Words, request.Limits, partition.WritingSystemId, rows =>
            {
                if (request.ReviewSnapshot is not { } snapshot) return;
                var measuredWords = rows.Select(row => new SIL.Motif.Host.Parser.AssessedWord(row.Word,
                    row.InvalidShape ? "skipped" : row.TimedOut ? "timed-out" : row.Capped ? "stopped" :
                    row.Analyses.Count > 0 || row.Unavailable.Count > 0 ? "analysed" : "no-analysis", []) { Morphology = row });
                partialRows.AddRange(TrialReviewEvidence.Observe(snapshot, partition.CaseKeys, measuredWords));
            }); }
            catch (Exception exception) when (onBaselineUnavailable is not null && exception is not OperationCanceledException)
            {
                onBaselineUnavailable(exception.Message);
                return JobCommands.EnqueueTrialAdmitted(RetainBefore("assess.unavailable"), expectedParserPath, admission);
            }
            if (!before.Succeeded)
            {
                if (before.Refusal!.Reason == FailureReason.StoppedByBudget)
                    return JobCommands.EnqueueStoppedTrial(RetainBefore(before.Refusal.Code) with { Words = words }, expectedParserPath, before.Refusal);
                if (request.ReviewSnapshot is not null &&
                    (before.Refusal.Reason == FailureReason.Cancelled || before.Refusal.Code == RefusalCodes.AssessParserMemoryLimit))
                    return JobCommands.EnqueueStoppedTrial(RetainBefore(before.Refusal.Code) with { Words = words }, expectedParserPath, before.Refusal);
                if (onBaselineUnavailable is null || before.Refusal.Reason == FailureReason.Cancelled)
                    return CommandOutcome<JobEnqueuedResponse>.Refused(before.Refusal);
                onBaselineUnavailable(before.Refusal!.Message);
                return JobCommands.EnqueueTrialAdmitted(RetainBefore(before.Refusal.Code), expectedParserPath, admission);
            }
            assessmentIds.AddRange(before.Value!.AssessmentIds);
            baselines.Add(new(partition.WritingSystemId, partition.CaseKeys, before.Value.AssessmentIds));
        }
        return JobCommands.EnqueueTrialAdmitted(request with
        {
            Words = words,
            BaselineAssessmentIds = assessmentIds,
            ReviewBaselines = request.ReviewSnapshot is null ? null : baselines,
        }, expectedParserPath, admission);

        EnqueueTrialRequest RetainBefore(string code) => request.ReviewSnapshot is null ? request : request with
        {
            BaselineAssessmentIds = assessmentIds.ToArray(),
            ReviewBaselines = baselines.ToArray(),
            ReviewBeforeStopCode = code,
            ReviewBeforePartial = partialRows.ToArray(),
        };
    }

    private sealed record SelectionPresent;
}
