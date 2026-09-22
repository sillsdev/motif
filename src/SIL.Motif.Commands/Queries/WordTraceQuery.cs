using SIL.Motif.Host;
using SIL.Motif.Contract.Responses;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>Runs a live trace or loads a saved trace without requiring a parser for the latter.</summary>
public static class WordTraceQuery
{
    public static CommandOutcome<WordTraceResponse> LoadDiagnostic(string json, int elapsedMs = 0, TraceHostCapture? current = null) =>
        WordTraceDiagnosticReader.Read(json, elapsedMs, current);

    public static CommandOutcome<WordTraceResponse> Query(
        WordTraceRequest request, CancellationToken cancellationToken = default)
    {
        using var invoker = new PanGlossInvoker();
        return Query(request, new PanGlossTracer(invoker), cancellationToken);
    }

    internal static CommandOutcome<WordTraceResponse> Query(
        WordTraceRequest request, IPanGlossTracer tracer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tracer);

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var baseline = new BaselineRepository(database).GetCurrent(workspaceKey);
            if (baseline is null)
            {
                return CommandOutcome<WordTraceResponse>.Refused(new Refusal(
                    "wordtrace.no-baseline", FailureReason.Refused,
                    "The project has no Baseline yet; capture one before tracing a word."));
            }

            var clock = Stopwatch.StartNew();
            var outcome = tracer.TraceAsync(baseline.FwDataPath, request.Word, cancellationToken)
                .GetAwaiter().GetResult();
            var elapsedMs = (int)clock.ElapsedMilliseconds;

            var result = outcome switch
            {
                PanGlossTraceOutcome.Completed completed => LiveSuccess(
                    completed.Document,
                    completed.Word,
                    completed.Tree,
                    completed.Summary,
                    complete: true,
                    stopReason: null,
                    completed.Details,
                    elapsedMs),
                PanGlossTraceOutcome.Incomplete incomplete => LiveSuccess(
                    incomplete.Document,
                    incomplete.Word,
                    incomplete.Tree,
                    incomplete.Summary,
                    complete: false,
                    stopReason: incomplete.Reason,
                    incomplete.Details,
                    elapsedMs),
                PanGlossTraceOutcome.Cancelled => CommandOutcome<WordTraceResponse>.Refused(new Refusal(
                    "wordtrace.cancelled", FailureReason.Cancelled, "The trace was cancelled.")),
                PanGlossTraceOutcome.Declined declined => CommandOutcome<WordTraceResponse>.Refused(new Refusal(
                    "wordtrace.parser-refused", FailureReason.Refused, declined.Detail)),
                PanGlossTraceOutcome.Unavailable unavailable => CommandOutcome<WordTraceResponse>.Refused(new Refusal(
                    "wordtrace.parser-unavailable", FailureReason.Refused, unavailable.Detail)),
                PanGlossTraceOutcome.Malformed malformed => CommandOutcome<WordTraceResponse>.Refused(new Refusal(
                    "wordtrace.malformed-output", FailureReason.Refused, malformed.Detail)),
                _ => CommandOutcome<WordTraceResponse>.Refused(new Refusal(
                    "wordtrace.parser-unavailable", FailureReason.Refused, outcome.Message)),
            };
            return result.Succeeded ? CommandOutcome<WordTraceResponse>.Success(
                TraceDiagnosticCapture.Attach(result.Value!, baseline, project)) : result;
        });
    }

    private static CommandOutcome<WordTraceResponse> LiveSuccess(
        PanGlossTraceDiagnosticDocument? document,
        string word,
        PanGlossTraceNode? root,
        PanGlossTraceSummary? summary,
        bool complete,
        string? stopReason,
        PanGlossTraceDetails? details,
        int elapsedMs)
    {
        if (document is not null)
            return CommandOutcome<WordTraceResponse>.Success(
                TraceDiagnosticProjection.Build(document, complete, stopReason, elapsedMs));

        var response = new WordTraceResponse(
            word,
            false,
            complete,
            stopReason,
            summary?.StepCount ?? 0,
            summary?.DeepestRuleReached,
            elapsedMs,
            [],
            root is null ? new TraceStep("NoTrace", null, null, null, null, []) : ConvertTree(root))
        {
            ParserSteps = details?.Steps,
            ParserElapsedMs = details is null ? null : details.ElapsedNs / 1_000_000.0,
            Guessed = details?.Guessed ?? false,
            SearchStatus = complete ? "complete" : "incomplete",
            InvalidShape = details?.InvalidShape ?? false,
        };
        return CommandOutcome<WordTraceResponse>.Success(response);
    }

    private static TraceStep ConvertTree(PanGlossTraceNode node) =>
        new(node.Type, node.Source, node.InputShape, node.OutputShape, node.FailureReason,
            node.Children.Select(ConvertTree).ToArray())
        {
            Subrule = node.Subrule,
            OutcomeStatus = node.OutcomeStatus,
            OutcomeEventType = node.OutcomeEventType,
            ContextualFailure = node.FailureContext,
            FailureRequired = node.FailureRequired,
            FailureActual = node.FailureActual,
            FailureEnvironment = node.FailureEnvironment,
            AttemptedMorphs = node.AttemptedMorphs.Select(TraceDiagnosticProjection.ToMorph).ToArray(),
            SourceIdentityId = node.SourceIdentityId,
        };
}
