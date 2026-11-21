using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Installation;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands;

/// <summary>How hard <see cref="TrialWorkflow.Enqueue(EnqueueTrialRequest, string, string?, BaselineMode, CancellationToken)"/> insists on a Baseline.</summary>
public enum BaselineMode
{
    /// <summary>A Baseline that cannot be assessed is reported and the Trial is queued without one.</summary>
    BestEffort,

    /// <summary>A Baseline that cannot be assessed ends the command before a job is queued.</summary>
    Required,
}

/// <summary>A queued Trial and, when it has no Baseline Assessments, why.</summary>
/// <param name="Job">The queued Trial job.</param>
/// <param name="BaselineWarning">Null when the Trial carries its Baseline; otherwise why it will have no Difference.</param>
/// <param name="Touched">The touched words a <c>--touched</c> Trial added; null when it did not ask.</param>
public sealed record TrialEnqueued(JobEnqueuedResponse Job, string? BaselineWarning, TouchedWords? Touched = null);

/// <summary>
/// Queues a Trial together with its Baseline Assessments: the Trial's words are assessed against the project's
/// current published Baseline first and those Assessment ids are bound to the job, giving the Trial a before side.
/// </summary>
public static class TrialWorkflow
{
    /// <summary>
    /// Queues <paramref name="request"/> with its Baseline Assessments. A request that already names
    /// <see cref="EnqueueTrialRequest.BaselineAssessmentIds"/> is queued as it stands.
    /// </summary>
    /// <param name="request">The Trial to queue; words are derived from the Proposal when it names none.</param>
    /// <param name="managedRoot">The root holding the project's managed workspace.</param>
    /// <param name="parserPath">The parser to assess with, or <see langword="null"/> to locate one.</param>
    /// <param name="mode">Whether a missing Baseline is a warning or a refusal.</param>
    /// <param name="cancellationToken">Cancels the Baseline measurement.</param>
    public static CommandOutcome<TrialEnqueued> Enqueue(EnqueueTrialRequest request, string managedRoot,
        string? parserPath, BaselineMode mode, CancellationToken cancellationToken) =>
        Enqueue(request, managedRoot, parserPath, mode, cancellationToken, requireExplicitRefresh: false);

    internal static CommandOutcome<TrialEnqueued> Enqueue(EnqueueTrialRequest request, string managedRoot,
        string? parserPath, BaselineMode mode, CancellationToken cancellationToken, bool requireExplicitRefresh) =>
        TouchedNeedsWords(request) is { } noWords ? CommandOutcome<TrialEnqueued>.Refused(noWords) :
        EnqueueWithUpdateGate(() => TrialBaselines.WithAdmission(request = request.WithPrequeueClock(), (admission, busy) =>
        {
            TouchedWords? touched = null;
            if (request.Touched)
            {
                var expanded = ExpandTouched(request);
                if (!expanded.Succeeded) return CommandOutcome<TrialEnqueued>.Refused(expanded.Refusal!);
                (request, touched) = (expanded.Value!.Request, expanded.Value.Touched);
            }
            var resolved = JobCommands.ResolveTrialWords(request);
            if (!resolved.Succeeded) return CommandOutcome<TrialEnqueued>.Refused(resolved.Refusal!);
            if (resolved.Value is null && !request.AllWords && mode == BaselineMode.Required &&
                request.BaselineAssessmentIds is null && TrialBaselines.RequireDefault(request) is { } missing)
                return CommandOutcome<TrialEnqueued>.Refused(missing);
            if (busy is null && request.BaselineAssessmentIds is null)
            {
                var baseline = TrialBaselines.EnsureCurrentBaseline(request, managedRoot, requireExplicitRefresh);
                if (!baseline.Succeeded &&
                    (mode == BaselineMode.Required || baseline.Refusal!.Code == RefusalCodes.ChangeRefreshRequired))
                    return CommandOutcome<TrialEnqueued>.Refused(baseline.Refusal!);
            }
            var enqueued = EnqueueCore(request,
                (words, limits, writingSystem, onPartial, earlier, onDispatched) => busy is null
                    ? TrialBaselines.Assess(request.FwDataPath, words, managedRoot, parserPath, cancellationToken,
                        limits, admission, writingSystem, onPartial, earlier, onDispatched, requireExplicitRefresh,
                        request.NoBackground)
                    : CommandOutcome<AssessCommandResponse>.Refused(busy),
                mode, parserPath ?? SIL.Motif.Host.Parser.PanGlossExecutable.TryLocate(), admission);
            return touched is null || !enqueued.Succeeded ? enqueued
                : CommandOutcome<TrialEnqueued>.Success(enqueued.Value! with { Touched = touched });
        }));

    private sealed record ExpandedTrial(EnqueueTrialRequest Request, TouchedWords Touched);

    private static Refusal? TouchedNeedsWords(EnqueueTrialRequest request) =>
        request.Touched && (request.Words is not { Count: > 0 } || request.AllWords)
            ? new Refusal(RefusalCodes.TrialTouchedNeedsWords, FailureReason.InvalidArgument,
                "--touched adds to a word list, and none was given. Next: pass --words <file>, or leave --touched out.")
            : null;

    // The touched words join the caller's own list, which keeps its order and comes first.
    private static CommandOutcome<ExpandedTrial> ExpandTouched(EnqueueTrialRequest request)
    {
        var touched = TrialTouchedWords.Resolve(request.FwDataPath, request.ProductVersion, request.ProposalId);
        if (!touched.Succeeded) return CommandOutcome<ExpandedTrial>.Refused(touched.Refusal!);
        var words = request.Words!.Concat(touched.Value!.Words).Distinct(StringComparer.Ordinal).ToArray();
        return CommandOutcome<ExpandedTrial>.Success(new ExpandedTrial(request with { Words = words }, touched.Value!));
    }

    /// <summary>As the public overload, with the Baseline measurement supplied by the caller.</summary>
    internal static CommandOutcome<TrialEnqueued> Enqueue(EnqueueTrialRequest request,
        Func<IReadOnlyList<string>, CommandOutcome<AssessCommandResponse>> assess, BaselineMode mode,
        string? expectedParserPath = null) =>
        Enqueue(request, (words, _) => assess(words), mode, expectedParserPath);

    /// <summary>Runs the update-gated workflow while carrying the frozen limits through the before measurement.</summary>
    internal static CommandOutcome<TrialEnqueued> Enqueue(EnqueueTrialRequest request,
        Func<IReadOnlyList<string>, TrialLimits?, CommandOutcome<AssessCommandResponse>> assess, BaselineMode mode,
        string? expectedParserPath = null, ProjectParseLease? admission = null) =>
        EnqueueWithUpdateGate(() => EnqueueCore(request, (words, limits, _, _, _, _) => assess(words, limits), mode, expectedParserPath, admission));

    private static CommandOutcome<TrialEnqueued> EnqueueWithUpdateGate(Func<CommandOutcome<TrialEnqueued>> enqueue)
    {
        // The lease covers the assess step too, because it runs the installed PanGloss binary an update replaces.
        using var lease = MotifUpdateGate.TryAcquire();
        if (lease is null)
            return CommandOutcome<TrialEnqueued>.Refused(new Refusal(RefusalCodes.TrialUpdateInProgress,
                FailureReason.Busy, "Motif is installing an update. Try the Trial again in a moment."));
        return enqueue();
    }

    private static CommandOutcome<TrialEnqueued> EnqueueCore(EnqueueTrialRequest request,
        TrialBaselines.BeforeAssessor assess, BaselineMode mode,
        string? expectedParserPath, ProjectParseLease? admission)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? warning = null;
        var failed = false;
        string? failure = null;
        CommandOutcome<JobEnqueuedResponse> queued;
        try
        {
            queued = TrialBaselines.EnqueueWithBaseline(request, (words, limits, writingSystem, onPartial, earlier, onDispatched) =>
            {
                try { return assess(words, limits, writingSystem, onPartial, earlier, onDispatched); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failed = true;
                    failure = exception.Message;
                    throw;
                }
            }, mode == BaselineMode.BestEffort ? reason => warning ??= reason : null, expectedParserPath, admission);
        }
        catch (Exception) when (failed && mode == BaselineMode.Required)
        {
            return CommandOutcome<TrialEnqueued>.Refused(Unavailable(failure!));
        }
        if (!queued.Succeeded) return CommandOutcome<TrialEnqueued>.Refused(queued.Refusal!);
        return CommandOutcome<TrialEnqueued>.Success(new TrialEnqueued(queued.Value!, warning));
    }

    private static Refusal Unavailable(string reason) => new(RefusalCodes.DifferenceNoBefore, FailureReason.Refused,
        "The Trial needs a Baseline Assessment to be compared with, and none could be taken: " + reason);
}
