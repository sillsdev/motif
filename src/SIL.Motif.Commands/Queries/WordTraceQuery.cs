using SIL.Motif.Host;
using SIL.Motif.Contract.Responses;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Commands.Store;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>Runs a live trace or loads a saved trace without requiring a parser for the latter.</summary>
public static class WordTraceQuery
{
    public static CommandOutcome<WordTraceResponse> LoadDiagnostic(string json, int elapsedMs = 0, TraceHostCapture? current = null) =>
        WordTraceDiagnosticReader.Read(json, elapsedMs, current);

    /// <summary>Reads a saved trace file into the same response a live trace gives, with no project and no parser.</summary>
    public static CommandOutcome<WordTraceResponse> Load(WordTraceLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string json;
        try
        {
            json = File.ReadAllText(request.DiagnosticPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
        {
            return CommandOutcome<WordTraceResponse>.Refused(new Refusal(
                "wordtrace.diagnostic-unreadable", FailureReason.NotFound,
                $"Motif could not read the saved trace '{request.DiagnosticPath}': {exception.Message}"));
        }
        return LoadDiagnostic(json);
    }

    public static CommandOutcome<WordTraceResponse> Query(
        WordTraceRequest request, CancellationToken cancellationToken = default)
    {
        return Query(request, PanGlossExecutable.TryLocate(), cancellationToken);
    }

    /// <summary>
    /// Traces a word for a caller that shows only the parser's answer. It skips the copy of the Baseline's FieldWorks
    /// project that supplies writing systems, FieldWorks links and captured names, which costs seconds on a large lexicon.
    /// </summary>
    public static CommandOutcome<WordTraceResponse> QueryAnswerOnly(
        WordTraceRequest request, CancellationToken cancellationToken = default, string? parserPath = null)
    {
        var parser = parserPath ?? PanGlossExecutable.TryLocate();
        using var invoker = new PanGlossInvoker(parser);
        if (!TryWordSessions.Enabled)
            return Query(request, new PanGlossTracer(invoker), cancellationToken, hostEvidence: false);
        using var tracer = TryWordSessions.TracerFor(parser, invoker);
        return Query(request, tracer, cancellationToken, hostEvidence: false);
    }

    /// <summary>Traces a word with an explicitly selected parser.</summary>
    /// <param name="request">The project and word to trace.</param>
    /// <param name="cancellationToken">Cancels the trace.</param>
    /// <param name="onProgress">Receives one "Tracing the word" report before the trace, on the cold and the warm
    /// path alike; neither path reports finer parser progress.</param>
    /// <param name="parserPath">The parser to run, or <see langword="null"/> when none is available.</param>
    /// <remarks>Uses the project's resident session when <see cref="TryWordSessions.Enabled"/>, else one process per word.</remarks>
    public static CommandOutcome<WordTraceResponse> Query(
        WordTraceRequest request, string? parserPath, CancellationToken cancellationToken = default,
        Action<AssessmentProgress>? onProgress = null) =>
        Query(request, parserPath, cancellationToken, onProgress, TryWordSessions.Enabled);

    internal static CommandOutcome<WordTraceResponse> Query(
        WordTraceRequest request, string? parserPath, CancellationToken cancellationToken,
        Action<AssessmentProgress>? onProgress, bool warm)
    {
        using var invoker = new PanGlossInvoker(parserPath);
        if (!warm) return Query(request, new PanGlossTracer(invoker), cancellationToken, onProgress);
        using var tracer = TryWordSessions.TracerFor(parserPath, invoker);
        return Query(request, tracer, cancellationToken, onProgress);
    }

    internal static CommandOutcome<WordTraceResponse> Query(
        WordTraceRequest request, IPanGlossTracer tracer, CancellationToken cancellationToken,
        Action<AssessmentProgress>? onProgress = null, bool hostEvidence = true)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tracer);

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            using var parseLease = ProjectParseLease.TryAcquire(database);
            if (parseLease is null)
                return CommandOutcome<WordTraceResponse>.Refused(ProjectParseLease.BusyRefusal());
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var currentBaseline = new BaselineRepository(database).GetCurrentPublished(workspaceKey);
            var baseline = currentBaseline;
            if (request.ProposalId is { } proposalId)
            {
                if (!SIL.Motif.Contract.Ids.CanonicalId.TryParse(proposalId, out _))
                    return CommandOutcome<WordTraceResponse>.Refused(new Refusal("proposal.invalid-id",
                        FailureReason.InvalidArgument,
                        $"'{proposalId}' is not a Proposal id. Use the id motif_start_proposal or motif_proposals gave.",
                        new Dictionary<string, string> { ["proposalId"] = proposalId }));
                var trial = Trials.LatestFor(database, project, proposalId);
                if (trial is null)
                    return CommandOutcome<WordTraceResponse>.Refused(new Refusal("trial.none", FailureReason.NotFound,
                        "No Trial is recorded for this Proposal."));
                var scratchRoot = TrialScratchPath.ForJob(trial.JobId);
                var scratchProject = TrialScratchPath.ProjectFile(scratchRoot);
                if (scratchProject is null)
                    return CommandOutcome<WordTraceResponse>.Refused(new Refusal("trial.scratch-gone",
                        FailureReason.NotFound, "The Trial copy was discarded. Set MOTIF_RETAIN_TRIAL_SCRATCH=1 " +
                        "in the runner environment and run the Trial again to trace it."));
                baseline = new BaselineRecord(workspaceKey, trial.Baseline, scratchRoot, scratchProject,
                    currentBaseline?.SourceLastWriteUtc ?? DateTimeOffset.MinValue,
                    currentBaseline?.PublishedUtc ?? DateTimeOffset.MinValue);
            }
            if (baseline is null)
            {
                return CommandOutcome<WordTraceResponse>.Refused(new Refusal(
                    "wordtrace.no-baseline", FailureReason.Refused,
                    "The project has no Baseline yet. Run 'motif baseline capture --project <fwdata>' before tracing a word."));
            }

            onProgress?.Invoke(new AssessmentProgress(AssessmentStage.Parsing, 0, 1, $"Tracing {request.Word}")
                { CurrentWord = request.Word });
            var clock = Stopwatch.StartNew();
            // A Proposal's Trial copy is scratch, so it never gets a resident session.
            var outcome = tracer is IWarmPanGlossTracer warm && request.ProposalId is null
                ? warm.TraceAsync(request.ProjectPath, TryWordSessions.TokenText(baseline.Token), baseline.FwDataPath,
                    request.Word, cancellationToken).GetAwaiter().GetResult()
                : tracer.TraceAsync(baseline.FwDataPath, request.Word, cancellationToken).GetAwaiter().GetResult();
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
                PanGlossTraceOutcome.Declined declined => CommandOutcome<WordTraceResponse>.Refused(
                    declined.ParserRefusal is { } failure
                        ? ParserExecutionRefusal.From(RefusalCodes.WordTraceParserRefused, request.ProjectPath, failure)
                        : new Refusal("wordtrace.parser-refused", FailureReason.Refused, declined.Detail,
                            parserDiagnostic: ParserCompileDiagnosticReader.Read(declined.Detail))),
                PanGlossTraceOutcome.Unavailable unavailable => CommandOutcome<WordTraceResponse>.Refused(new Refusal(
                    "wordtrace.parser-unavailable", FailureReason.Refused, unavailable.Detail,
                    ParserNotFoundFact.Mark(null, unavailable.ExecutableMissing))),
                PanGlossTraceOutcome.Malformed malformed => CommandOutcome<WordTraceResponse>.Refused(new Refusal(
                    "wordtrace.malformed-output", FailureReason.Refused, malformed.Detail)),
                _ => CommandOutcome<WordTraceResponse>.Refused(new Refusal(
                    "wordtrace.parser-unavailable", FailureReason.Refused, outcome.Message)),
            };
            var refusals = new ParserRefusalRepository(database);
            if (result.Succeeded && (outcome.Steps is not null || outcome.WorkSteps is not null))
                result = CommandOutcome<WordTraceResponse>.Success(result.Value! with
                {
                    ParserSteps = outcome.Steps ?? result.Value.ParserSteps,
                    ParserWorkSteps = outcome.WorkSteps ?? result.Value.ParserWorkSteps,
                });
            if (result.Succeeded) refusals.Clear(baseline.Token);
            else if (result.Refusal?.ParserDiagnostic is not null) refusals.Save(baseline.Token, result.Refusal);
            return result.Succeeded ? CommandOutcome<WordTraceResponse>.Success(
                TraceDiagnosticCapture.Attach(result.Value!, baseline, project, elapsedMs, hostEvidence)) : result;
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
            TraceReadingBuilder.Build(word,
                root is null ? new TraceStep("NoTrace", null, null, null, null, []) : ConvertTree(root), [], []))
        {
            ParserSteps = details?.Steps,
            ParserWorkSteps = details?.WorkSteps,
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
            AttemptedMorphs = node.AttemptedMorphs.Select(morph => TraceReadingBuilder.ToMorph(morph)).ToArray(),
            FailureEvidence = node.FailureEvidence,
            EventEvidence = node.EventEvidence,
            SourceIdentityKind = node.SourceIdentityKind,
            SourceIdentityId = node.SourceIdentityId,
            SourceIdentityQuality = node.SourceIdentityQuality,
        };
}
