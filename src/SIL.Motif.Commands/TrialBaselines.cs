using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Projects;

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
        AssessCommand.Assess(new AssessRequest(projectPath,
            words.Count == 0 ? null : new SelectionRequest(false, [], words, false, null)),
            managedRoot, parserPath ?? PanGlossExecutable.TryLocate(), null, cancellationToken);

    /// <summary>
    /// Queues a Trial that carries its Baseline Assessments, so <c>difference produce</c> has a before side.
    /// Words are resolved the way <see cref="JobCommands.EnqueueTrial"/> resolves them, then assessed first.
    /// </summary>
    public static CommandOutcome<JobEnqueuedResponse> EnqueueWithBaseline(EnqueueTrialRequest request,
        string managedRoot, string? parserPath, CancellationToken cancellationToken) =>
        EnqueueWithBaseline(request, words => Assess(request.FwDataPath, words, managedRoot, parserPath,
            cancellationToken));

    /// <summary>As the other overload, with the Baseline measurement supplied by the caller.</summary>
    internal static CommandOutcome<JobEnqueuedResponse> EnqueueWithBaseline(EnqueueTrialRequest request,
        Func<IReadOnlyList<string>, CommandOutcome<AssessCommandResponse>> assess)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BaselineAssessmentIds is not null) return JobCommands.EnqueueTrial(request);
        var resolved = JobCommands.ResolveTrialWords(request);
        if (!resolved.Succeeded) return CommandOutcome<JobEnqueuedResponse>.Refused(resolved.Refusal!);
        var words = resolved.Value;
        var before = assess(words ?? []);
        if (!before.Succeeded) return CommandOutcome<JobEnqueuedResponse>.Refused(before.Refusal!);
        return JobCommands.EnqueueTrial(request with
        {
            Words = words,
            BaselineAssessmentIds = before.Value!.AssessmentIds,
        });
    }
}
