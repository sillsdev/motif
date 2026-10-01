using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads recorded parser evidence without opening a project or invoking a parser.</summary>
public static class TraceReadingBuilder
{
    public static WordTraceReading Build(PanGlossTraceDiagnosticDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document.Root is null ? new TraceStep("NoTrace", null, null, null, null, [])
            { StepId = "0" } : ConvertTree(document.Root, "0");
        var attempts = document.Root is null ? [] : BuildCandidates(document.Root, document.Attempts).ToArray();
        var signatures = document.Signature.Split(';');
        var analyses = document.Analyses.Select((analysis, index) => ToAnalysis(analysis) with
        {
            Signature = signatures.Length == document.Analyses.Count ? signatures[index] : AnalysisSignature(analysis),
        }).ToArray();
        var distinct = analyses.GroupBy(analysis => analysis.Signature, StringComparer.Ordinal)
            .Select(group => group.First() with
            {
                FoundWays = group.Count(),
                ProducerAnalysisIds = group.Select(analysis => analysis.AnalysisId).OfType<string>().ToArray(),
            }).ToArray();
        return Summarize(document.Word, root, attempts, distinct);
    }

    /// <summary>Reads responses with no producer document, including a search interrupted before any evidence returned.</summary>
    public static WordTraceReading Build(WordTraceResponse response) => response.Reading ??
        Summarize(response.Word, response.Root, response.Candidates, response.Analyses);

    internal static WordTraceReading Summarize(string word, TraceStep root,
        IReadOnlyList<TraceCandidate> attempts, IReadOnlyList<TraceAnalysis> analyses)
    {
        var closest = attempts.Where(IsFailure)
            .OrderByDescending(candidate => candidate.Morphs.Count(morph => !string.IsNullOrWhiteSpace(morph.Form) && morph.Form != "?"))
            .ThenByDescending(candidate => candidate.Steps.Count).ToArray();
        var stops = closest.GroupBy(candidate => (Identity: candidate.StoppedByRuleId ?? candidate.StoppedByRule, candidate.FailureReason))
            .Select(group => new TraceStopGroup(group.First().StoppedByRule, group.First().StoppedByRuleId,
                group.Key.FailureReason, group.First().Explanation, group.ToArray()))
            .OrderByDescending(group => group.Count).ToArray();
        var best = attempts.FirstOrDefault(candidate => candidate.Succeeded) ?? closest.FirstOrDefault();
        var rules = best is null ? [] : Rules(best);
        return new WordTraceReading(word, root, attempts, analyses, stops, closest, rules);
    }

    private static string AnalysisSignature(PanGlossTraceAnalysis analysis) =>
        JsonSerializer.Serialize(new { analysis.LegacyMorphemes, analysis.Surface,
            Morphs = analysis.Morphs.Select(morph => new { morph.FormId, morph.EntryId, morph.MsaId,
                morph.InflTypeId, morph.MorphemeId, morph.AllomorphId, morph.GuessedString }) });

    private static bool IsFailure(TraceCandidate candidate) => !candidate.Succeeded &&
        candidate.OutcomeStatus != "blocked" && !candidate.Steps.Any(step => step.Type == "Blocked" && step == candidate.Steps.LastOrDefault()) &&
        (candidate.OutcomeStatus is "failed" or "failure" || candidate.FailureReason is { Length: > 0 } ||
         candidate.ContextualFailure is { Length: > 0 } || candidate.Steps.Any(step => step.FailureReason is { Length: > 0 }));

    private static TraceRuleReading[] Rules(TraceCandidate best)
    {
        var building = best.Steps.Where(step => step.Type.Contains("Synthesis", StringComparison.Ordinal)).ToArray();
        var path = building.Length > 0 ? building : best.Steps;
        return path
        .Where(step => !string.IsNullOrWhiteSpace(step.Source) &&
            (step.Type.Contains("Rule", StringComparison.Ordinal) || step.Type.Contains("Template", StringComparison.Ordinal)))
        .GroupBy(step => step.SourceIdentityId ?? step.Source!, StringComparer.Ordinal)
        .Select(group => new TraceRuleReading(group.First().Source!, group.First().SourceIdentityId,
            string.Join(" · ", group.Select(step => Kind(step.Type)).Distinct(StringComparer.Ordinal)),
            group.Any(step => step.Type == "Blocked" || step.OutcomeStatus == "blocked") ? "not repeated (would feed itself)"
                : group.Any(step => step.FailureReason is { Length: > 0 }) ? "stopped" : best.Succeeded ? "applied" : "tried",
            Explain(group.ToArray(), best.Steps), group.Select(step => step.StepId).ToArray())).ToArray();
    }

    private static string Kind(string type) => type.Contains("PhonologicalRule", StringComparison.Ordinal) ? "Phonological rule"
        : type.Contains("MorphologicalRule", StringComparison.Ordinal) ? "Affix rule"
        : type.Contains("CompoundingRule", StringComparison.Ordinal) ? "Compound rule"
        : type.Contains("Template", StringComparison.Ordinal) ? "Affix template" : type;

    private static string Explain(IReadOnlyList<TraceStep> steps, IReadOnlyList<TraceStep> path)
    {
        var failed = steps.FirstOrDefault(step => step.FailureReason is { Length: > 0 });
        if (failed is not null) return failed.ContextualFailure is { Length: > 0 } context && context != "unavailable" ? context
            : HermitCrabFailureExplanations.Explain(failed.FailureReason!) ?? failed.FailureReason!;
        var synthesis = steps.Where(step => step.Type.Contains("Synthesis", StringComparison.Ordinal)).ToArray();
        foreach (var step in synthesis.Length > 0 ? synthesis : steps)
        {
            if (step.Output is not { Length: > 0 } output) continue;
            var input = step.Input;
            if (string.IsNullOrEmpty(input))
            {
                var index = path.ToList().IndexOf(step);
                input = path.Take(index).Reverse().Select(previous => previous.Output ?? previous.Input)
                    .FirstOrDefault(form => !string.IsNullOrEmpty(form));
            }
            if (string.IsNullOrEmpty(input) || input == output) continue;
            var prefix = output.EndsWith(input, StringComparison.Ordinal) ? output[..^input.Length] : null;
            var suffix = output.StartsWith(input, StringComparison.Ordinal) ? output[input.Length..] : null;
            var affix = prefix == step.Source ? prefix + "-" : suffix == step.Source ? "-" + suffix : null;
            return (affix is null ? "" : affix + " · ") + $"{input} → {output}";
        }
        return steps.Select(step => step.Output ?? step.Input).FirstOrDefault(form => !string.IsNullOrEmpty(form)) ?? "—";
    }

    internal static TraceAnalysis ToAnalysis(PanGlossTraceAnalysis analysis) =>
        new(analysis.AnalysisId, analysis.Index, analysis.Surface, analysis.Availability,
            analysis.Morphs.Select(ToMorph).ToArray())
        {
            LegacyMorphemes = analysis.LegacyMorphemes,
            ProjectionStatus = analysis.ProjectionStatus,
            ProjectionError = analysis.ProjectionError,
        };

    internal static TraceMorph ToMorph(PanGlossTraceMorph morph) =>
        new(morph.Identity, morph.Form, morph.Headword, morph.Gloss, morph.Category, morph.Slot,
            morph.InflectionClass, morph.Features, morph.GuessedString, morph.FieldWorksLink)
        {
            FormId = morph.FormId,
            EntryId = morph.EntryId,
            MsaId = morph.MsaId,
            InflTypeId = morph.InflTypeId,
            IdentityQuality = morph.IdentityQuality,
            FormWritingSystem = morph.FormWritingSystem,
            HeadwordWritingSystem = morph.HeadwordWritingSystem,
            GlossWritingSystem = morph.GlossWritingSystem,
            CategoryId = morph.CategoryId,
            CategoryName = morph.CategoryName,
            CategoryAbbreviation = morph.CategoryAbbreviation,
            SlotId = morph.SlotId,
            SlotOptional = morph.SlotOptional,
            InflectionClassId = morph.InflectionClassId,
            InflectionClassName = morph.InflectionClassName,
            InflectionClassAbbreviation = morph.InflectionClassAbbreviation,
            FeaturesSource = morph.FeaturesSource,
            FeaturesStatus = morph.FeaturesStatus,
            MorphemeId = morph.MorphemeId,
            AllomorphId = morph.AllomorphId,
            SourceFormIds = morph.SourceFormIds,
            FormSourceId = morph.FormSourceId,
            HeadwordSourceId = morph.HeadwordSourceId,
            GlossSourceId = morph.GlossSourceId,
            RawJson = morph.Raw.GetRawText(),
        };

    private static List<TraceCandidate> BuildCandidates(
        PanGlossTraceNode root, IReadOnlyList<PanGlossTraceAttempt> attempts)
    {
        var candidates = new List<TraceCandidate>();
        var attemptIndex = 0;
        Walk(root, [root], [ConvertStep(root, "0")], "0");
        return candidates;

        void Walk(PanGlossTraceNode node, List<PanGlossTraceNode> path, List<TraceStep> story, string id)
        {
            if (node.Type is "Successful" or "Failed" || IsTerminalOutcome(node.OutcomeStatus))
            {
                var attempt = attemptIndex < attempts.Count ? attempts[attemptIndex++] : null;
                var succeeded = attempt?.Succeeded ??
                    node.OutcomeStatus is "success" or "succeeded" or "successful" || node.Type == "Successful";
                var stopper = succeeded ? null : StoppingStep(path);
                var reason = stopper?.FailureReason ?? attempt?.FailureReason ?? node.FailureReason;
                var morphs = attempt is { Morphs.Count: > 0 }
                    ? attempt.Morphs.Select(ToMorph).ToArray()
                    : MorphsReached(path, stopper).Select(ToMorph).ToArray();
                candidates.Add(new TraceCandidate(
                    morphs.Select(ToReadingMorph).ToArray(),
                    succeeded,
                    reason,
                    reason is null ? null : HermitCrabFailureExplanations.Explain(reason),
                    story.ToArray())
                {
                    Surface = node.OutputShape ?? node.InputShape ?? stopper?.InputShape,
                    StoppedByRule = stopper?.Source,
                    StoppedByRuleId = stopper?.SourceIdentityId,
                    RichMorphs = morphs,
                    MorphAvailability = morphs.Length > 0 ? "recorded" : "unavailable",
                    AttemptId = attempt?.AttemptId ?? id,
                    ContextualFailure = attempt?.FailureContext ?? node.FailureContext,
                    FailureRequired = attempt?.FailureRequired ?? node.FailureRequired,
                    FailureActual = attempt?.FailureActual ?? node.FailureActual,
                    FailureEnvironment = attempt?.FailureEnvironment ?? node.FailureEnvironment,
                    SourceIdentityKind = attempt?.SourceIdentityKind ?? node.SourceIdentityKind,
                    SourceIdentityId = attempt?.SourceIdentityId ?? node.SourceIdentityId,
                    SourceIdentityQuality = attempt?.SourceIdentityQuality ?? node.SourceIdentityQuality,
                    OutcomeStatus = attempt?.Status ?? node.OutcomeStatus,
                });
            }

            var earlier = new List<TraceStep>();
            for (var index = 0; index < node.Children.Count; index++)
            {
                var child = node.Children[index];
                var childId = $"{id}.{index}";
                Walk(child, [.. path, child], [.. story, .. earlier, ConvertStep(child, childId)], childId);
                if (child.Children.Count == 0 && child.Type is not ("Successful" or "Failed") &&
                    !IsTerminalOutcome(child.OutcomeStatus))
                    earlier.Add(ConvertStep(child, childId));
                if (child.Type is "Successful" or "Failed" || IsTerminalOutcome(child.OutcomeStatus))
                    earlier.RemoveAll(step => step.FailureReason is { Length: > 0 });
            }
        }
    }

    private static PanGlossTraceNode? StoppingStep(IReadOnlyList<PanGlossTraceNode> path)
    {
        for (var level = path.Count - 1; level > 0; level--)
        {
            var parent = path[level - 1];
            var index = IndexOf(parent.Children, path[level]);
            for (var sibling = index - 1; sibling >= 0; sibling--)
            {
                var candidate = parent.Children[sibling];
                if (candidate.FailureReason is { Length: > 0 } && candidate.Type is not ("Failed" or "Successful"))
                    return candidate;
                if (candidate.Type is "Failed" or "Successful") break;
            }
        }
        return null;
    }

    private static IReadOnlyList<PanGlossTraceMorph> MorphsReached(IReadOnlyList<PanGlossTraceNode> path, PanGlossTraceNode? stopper)
    {
        if (path[^1].AttemptedMorphs.Count > 0) return path[^1].AttemptedMorphs;
        if (stopper is { AttemptedMorphs.Count: > 0 }) return stopper.AttemptedMorphs;
        for (var level = path.Count - 2; level >= 0; level--)
            if (path[level].AttemptedMorphs.Count > 0) return path[level].AttemptedMorphs;
        return [];
    }

    private static int IndexOf(IReadOnlyList<PanGlossTraceNode> nodes, PanGlossTraceNode node)
    {
        for (var index = 0; index < nodes.Count; index++)
            if (ReferenceEquals(nodes[index], node)) return index;
        return -1;
    }

    internal static ParserReadingMorph ToReadingMorph(TraceMorph morph) => new(
        morph.Form ?? morph.GuessedString ?? "?",
        morph.Gloss ?? string.Empty,
        morph.CategoryAbbreviation ?? morph.Category ?? string.Empty,
        null,
        morph.GuessedString is not null,
        morph.FieldWorksLink);
    private static bool IsTerminalOutcome(string? status) => status is "successful" or "succeeded" or "success" or "failed" or "failure" or "blocked";

    private static TraceStep ConvertTree(PanGlossTraceNode node, string id) =>
        ConvertStep(node, id, node.Children.Select((child, index) => ConvertTree(child, $"{id}.{index}")).ToArray());

    private static TraceStep ConvertStep(PanGlossTraceNode node, string id) => ConvertStep(node, id, []);

    private static TraceStep ConvertStep(PanGlossTraceNode node, string id, IReadOnlyList<TraceStep> children) =>
        new(node.Type, node.Source, node.InputShape, node.OutputShape, node.FailureReason, children)
        {
            StepId = id,
            Subrule = node.Subrule,
            OutcomeStatus = node.OutcomeStatus,
            OutcomeEventType = node.OutcomeEventType,
            ContextualFailure = node.FailureContext,
            FailureRequired = node.FailureRequired,
            FailureActual = node.FailureActual,
            FailureEnvironment = node.FailureEnvironment,
            AttemptedMorphs = node.AttemptedMorphs.Select(ToMorph).ToArray(),
            SourceIdentityKind = node.SourceIdentityKind,
            SourceIdentityId = node.SourceIdentityId,
            SourceIdentityQuality = node.SourceIdentityQuality,
        };

}
