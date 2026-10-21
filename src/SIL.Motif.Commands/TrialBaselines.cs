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
        EnqueueWithBaseline(request, words => Assess(request.FwDataPath, words, managedRoot, parserPath,
            cancellationToken), onBaselineUnavailable, parserPath ?? SIL.Motif.Host.Parser.PanGlossExecutable.TryLocate());

    /// <summary>As the other overload, with the Baseline measurement supplied by the caller.</summary>
    internal static CommandOutcome<JobEnqueuedResponse> EnqueueWithBaseline(EnqueueTrialRequest request,
        Func<IReadOnlyList<string>, CommandOutcome<AssessCommandResponse>> assess,
        Action<string>? onBaselineUnavailable = null, string? expectedParserPath = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BaselineAssessmentIds is not null) return JobCommands.EnqueueTrial(request, expectedParserPath);
        var resolved = JobCommands.ResolveTrialWords(request);
        if (!resolved.Succeeded) return CommandOutcome<JobEnqueuedResponse>.Refused(resolved.Refusal!);
        var words = resolved.Value;
        CommandOutcome<AssessCommandResponse> before;
        try { before = assess(words ?? []); }
        catch (Exception exception) when (onBaselineUnavailable is not null && exception is not OperationCanceledException)
        {
            // A project that cannot be assessed here still queues its Trial; the caller is told what is missing.
            onBaselineUnavailable(exception.Message);
            return JobCommands.EnqueueTrial(request, expectedParserPath);
        }
        if (!before.Succeeded)
        {
            if (onBaselineUnavailable is null) return CommandOutcome<JobEnqueuedResponse>.Refused(before.Refusal!);
            onBaselineUnavailable(before.Refusal!.Message);
            return JobCommands.EnqueueTrial(request, expectedParserPath);
        }
        return JobCommands.EnqueueTrial(request with
        {
            Words = words,
            BaselineAssessmentIds = before.Value!.AssessmentIds,
        }, expectedParserPath);
    }
}
