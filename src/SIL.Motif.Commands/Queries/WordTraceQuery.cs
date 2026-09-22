using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Traces one word against a project's current Baseline grammar through <see cref="IPanGlossTracer"/>, and
/// maps the parser's own derivation tree to the candidates and plain-language explanations FieldWorks' Try
/// a Word dialog shows.
/// </summary>
/// <remarks>
/// <b>A candidate is one <c>Successful</c> or <c>Failed</c> outcome node's path</b>, paired with the most
/// recent <c>LexicalLookup</c> the traversal saw before reaching it — verified against a real captured
/// trace (<c>trace-format.md</c>'s worked example: <c>pg-rules/src/trace.rs</c>'s own output for <c>sagd</c>),
/// where the root lookup and its outcome are <em>siblings</em> under a shared rule-application parent
/// rather than the strict lookup-contains-outcome nesting the format's prose describes. PanGloss's trace
/// tree also carries no morpheme identity — no <c>MorphRA</c>/<c>MsaRA</c> GUID, only rule names and
/// surface-shape strings — so a candidate's single morph is the root form that lookup found, not the full
/// breakdown <see cref="SIL.Motif.Host.PanGloss.ParserReadingReader"/> resolves for a batch result; the
/// affix rules tried along the way remain visible only in <see cref="TraceCandidate.Steps"/>' own
/// <c>Source</c> names.
/// </remarks>
public static class WordTraceQuery
{
    /// <summary>Traces through a real parser invocation, resolving the executable the real installation uses.</summary>
    public static CommandOutcome<WordTraceResponse> Query(
        WordTraceRequest request, CancellationToken cancellationToken = default)
    {
        using var invoker = new PanGlossInvoker();
        return Query(request, new PanGlossTracer(invoker), cancellationToken);
    }

    /// <summary>
    /// Traces through an explicitly supplied tracer — a fake stands in for PanGloss in tests. Admission and
    /// containment are the invoker's, so this query holds no queue and no governor.
    /// </summary>
    internal static CommandOutcome<WordTraceResponse> Query(
        WordTraceRequest request, IPanGlossTracer tracer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tracer);

        return ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, project) =>
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

            return outcome switch
            {
                PanGlossTraceOutcome.Completed completed => CommandOutcome<WordTraceResponse>.Success(
                    Build(completed.Word, completed.Tree, completed.Summary, complete: true, stopReason: null, elapsedMs)),
                PanGlossTraceOutcome.Incomplete incomplete => CommandOutcome<WordTraceResponse>.Success(
                    Build(incomplete.Word, incomplete.Tree, incomplete.Summary, complete: false, incomplete.Reason, elapsedMs)),
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
        });
    }

    private static WordTraceResponse Build(
        string word, PanGlossTraceNode? root, PanGlossTraceSummary? summary, bool complete, string? stopReason, int elapsedMs)
    {
        var candidates = root is null ? Array.Empty<TraceCandidate>()
            : BuildCandidates(root).OrderByDescending(candidate => candidate.Succeeded)
                .ThenByDescending(candidate => candidate.Steps.Count).ToArray();
        var parsed = candidates.Any(candidate => candidate.Succeeded);
        var rootStep = root is null
            ? new TraceStep("NoTrace", null, null, null, null, Array.Empty<TraceStep>())
            : ConvertTree(root);
        return new WordTraceResponse(
            word, parsed, complete, stopReason, summary?.StepCount ?? 0, summary?.DeepestRuleReached, elapsedMs,
            candidates, rootStep);
    }

    private static TraceStep ConvertTree(PanGlossTraceNode node) => new(
        node.Type, node.Source, node.InputShape, node.OutputShape, node.FailureReason,
        node.Children.Select(ConvertTree).ToArray());

    // One candidate per Successful/Failed node, paired with the lookup in scope on its own branch.
    private static List<TraceCandidate> BuildCandidates(PanGlossTraceNode root)
    {
        var candidates = new List<TraceCandidate>();
        Walk(root, [root], null);
        return candidates;

        void Walk(PanGlossTraceNode node, List<PanGlossTraceNode> path, PanGlossTraceNode? inScope)
        {
            if (node.Type is "Successful" or "Failed") candidates.Add(BuildCandidate(path, inScope));
            // A lookup reaches its later siblings and their descendants, never a neighbouring branch.
            var scoped = node.Type == "LexicalLookup" ? node : inScope;
            foreach (var child in node.Children)
            {
                Walk(child, [.. path, child], scoped);
                if (child.Type == "LexicalLookup") scoped = child;
            }
        }
    }

    private static TraceCandidate BuildCandidate(IReadOnlyList<PanGlossTraceNode> path, PanGlossTraceNode? lookup)
    {
        var outcome = path[^1];
        var succeeded = outcome.Type == "Successful";
        var failureReason = succeeded ? null : path.AsEnumerable().Reverse()
            .Select(step => step.FailureReason).FirstOrDefault(reason => reason is not null);
        var explanation = failureReason is null ? null : HermitCrabFailureExplanations.Explain(failureReason);
        // The lookup that fed a sibling outcome is part of that path's story, so it sits just before the outcome.
        var walked = lookup is not null && !path.Contains(lookup) ? [.. path.Take(path.Count - 1), lookup, outcome] : path;
        var steps = walked
            .Select(node => new TraceStep(node.Type, node.Source, node.InputShape, node.OutputShape, node.FailureReason, []))
            .ToList();
        var form = lookup?.InputShape ?? lookup?.OutputShape ?? outcome.InputShape ?? outcome.OutputShape ?? "?";
        var morphs = new[] { new ParserReadingMorph(form, string.Empty, string.Empty, null, Guessed: false, FieldWorksLink: null) };
        return new TraceCandidate(morphs, succeeded, failureReason, explanation, steps);
    }

    private static string ResolveProductVersion() => MotifProductVersion.CurrentText;
}
