using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Installation;

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
public sealed record TrialEnqueued(JobEnqueuedResponse Job, string? BaselineWarning);

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
        Enqueue(request, words => TrialBaselines.Assess(request.FwDataPath, words, managedRoot, parserPath,
            cancellationToken), mode, parserPath ?? SIL.Motif.Host.Parser.PanGlossExecutable.TryLocate());

    /// <summary>As the public overload, with the Baseline measurement supplied by the caller.</summary>
    internal static CommandOutcome<TrialEnqueued> Enqueue(EnqueueTrialRequest request,
        Func<IReadOnlyList<string>, CommandOutcome<AssessCommandResponse>> assess, BaselineMode mode,
        string? expectedParserPath = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The lease covers the assess step too, because it runs the installed PanGloss binary an update replaces.
        using var lease = MotifUpdateGate.TryAcquire();
        if (lease is null)
            return CommandOutcome<TrialEnqueued>.Refused(new Refusal(RefusalCodes.TrialUpdateInProgress,
                FailureReason.Busy, "Motif is installing an update. Try the Trial again in a moment."));
        string? warning = null;
        var failed = false;
        string? failure = null;
        CommandOutcome<JobEnqueuedResponse> queued;
        try
        {
            queued = TrialBaselines.EnqueueWithBaseline(request, words =>
            {
                try { return assess(words); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failed = true;
                    failure = exception.Message;
                    throw;
                }
            }, mode == BaselineMode.BestEffort ? reason => warning ??= reason : null, expectedParserPath);
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
