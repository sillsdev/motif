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
        var analyses = document.Analyses.Select((analysis, index) => ToAnalysis(analysis, index) with
        {
            Signature = signatures.Length == document.Analyses.Count && signatures[index].Length > 0 ? signatures[index] : null,
        }).ToArray();
        return Summarize(document.Word, root, attempts, analyses);
    }

    /// <summary>Returns the response's authoritative display reading, including its captured enrichment.</summary>
    public static WordTraceReading Build(WordTraceResponse response) => response.Reading;

    /// <summary>Projects a tree and its recorded attempts and analyses into one authoritative display reading.</summary>
    public static WordTraceReading Build(string word, TraceStep root,
        IReadOnlyList<TraceCandidate> attempts, IReadOnlyList<TraceAnalysis> analyses) => Summarize(word, root, attempts, analyses);

    internal static WordTraceReading Summarize(string word, TraceStep root,
        IReadOnlyList<TraceCandidate> attempts, IReadOnlyList<TraceAnalysis> analyses)
    {
        attempts = attempts.Select((attempt, index) => attempt with { AttemptId = attempt.AttemptId ?? $"attempt-{index}" }).ToArray();
        var closest = attempts.Where(IsFailure)
            .OrderByDescending(candidate => candidate.Morphs.Count(morph => !string.IsNullOrWhiteSpace(morph.Form) && morph.Form != "?"))
            .ThenByDescending(candidate => candidate.Steps.Count).ToArray();
        var stops = closest.GroupBy(candidate => (Identity: candidate.StoppedByRefId ?? candidate.AttemptId, candidate.FailureReason))
            .Select(group => new TraceStopGroup(group.First().StoppedByRule, TraceRefIds.CanonicalIdentity(group.First().StoppedByRuleId),
                group.Key.FailureReason, group.First().Explanation, group.ToArray())
                { RuleRefId = group.First().StoppedByRefId })
            .OrderByDescending(group => group.Count).ToArray();
        var best = attempts.FirstOrDefault(candidate => candidate.Succeeded) ?? closest.FirstOrDefault();
        var rules = best is null ? [] : Rules(best);
        var distinct = DistinctAnalyses(analyses);
        return new WordTraceReading(word, root, attempts, distinct, stops, closest, rules)
        {
            Refs = Refs(root, attempts, distinct),
        };
    }

    // First mention wins the label, so a ref reads as the reading first shows it.
    private static TraceRef[] Refs(TraceStep root, IReadOnlyList<TraceCandidate> attempts, IReadOnlyList<TraceAnalysis> analyses)
    {
        var steps = new List<TraceStep>();
        Collect(root);
        foreach (var attempt in attempts) steps.AddRange(attempt.Steps);
        var affixKeys = steps.Where(step => step.SourceIdentityKind == "morphRule" && step.SourceIdentityId is not null)
            .Select(step => TraceRefIds.CanonicalIdentity(step.SourceIdentityId)!).ToHashSet(StringComparer.Ordinal);
        var morphs = analyses.SelectMany(analysis => analysis.Morphs)
            .Concat(steps.SelectMany(step => step.AttemptedMorphs))
            .Concat(attempts.SelectMany(attempt => attempt.RichMorphs));
        var refs = new Dictionary<string, TraceRef>(StringComparer.Ordinal);
        var order = new List<string>();
        void Add(TraceRef reference)
        {
            if (refs.TryAdd(reference.Id, reference)) order.Add(reference.Id);
        }
        foreach (var step in steps)
            if (step.RefId is { } id) Add(RuleRef(id, step));
        foreach (var morph in morphs)
            if (morph.RefId is { } id) Add(MorphRef(id, morph, affixKeys));
        return order.Select(id => refs[id]).ToArray();

        void Collect(TraceStep step)
        {
            steps.Add(step);
            foreach (var child in step.Children) Collect(child);
        }
    }

    private static TraceRef RuleRef(string id, TraceStep step)
    {
        var identityKind = step.SourceIdentityKind ?? TraceRefIds.IdentityKindOf(step.Type);
        var kind = identityKind switch
        {
            "phonRule" => "phonologicalRule",
            "morphRule" when step.Type.Contains("CompoundingRule", StringComparison.Ordinal) => "compoundRule",
            "morphRule" => "morphologicalRule",
            "compoundingRule" => "compoundRule",
            "template" or "affixTemplate" => "template",
            "stratum" => "stratum",
            _ => identityKind ?? "rule",
        };
        var identity = step.SourceIdentityKind is null ? null : TraceRefIds.CanonicalIdentity(step.SourceIdentityId);
        var timingKind = identity is null ? null : step.SourceIdentityKind switch
        {
            "morphRule" => "morph_rule",
            "phonRule" => "phon_rule",
            _ => null,
        };
        return new TraceRef(id, kind, step.Source ?? identity ?? id)
        {
            Identity = identity,
            IdentityQuality = identity is null ? TraceRefIds.UnknownQuality
                : step.SourceIdentityQuality ?? TraceRefIds.UnknownQuality,
            TimingKey = timingKind is null ? null : new TraceTimingKey(timingKind, identity!),
        };
    }

    // An affix is timed as the rule that adds it, keyed by its grammatical info; a stem as its entry.
    private static TraceRef MorphRef(string id, TraceMorph morph, IReadOnlySet<string> affixKeys)
    {
        var quality = morph.IdentityQuality ?? TraceRefIds.UnknownQuality;
        var keyed = quality is "authored" or "grammar-local";
        var timing = !keyed ? null
            : TraceRefIds.CanonicalIdentity(morph.MsaId) is { } msa && affixKeys.Contains(msa) ? new TraceTimingKey("morph_rule", msa)
            : TraceRefIds.CanonicalIdentity(morph.EntryId) is { } entry ? new TraceTimingKey("lex_entry", entry) : null;
        return new TraceRef(id, "morph", morph.Form ?? morph.GuessedString ?? morph.Headword ?? "?")
        {
            Gloss = morph.Gloss,
            Identity = TraceRefIds.CanonicalIdentity(morph.EntryId ?? morph.FormId ?? morph.MsaId),
            IdentityQuality = quality,
            TimingKey = timing,
        };
    }

    private static TraceAnalysis[] DistinctAnalyses(IReadOnlyList<TraceAnalysis> analyses) => analyses
        .GroupBy(analysis => analysis.Signature ?? AnalysisSignature(analysis), StringComparer.Ordinal)
        .Select(group => group.First() with
        {
            Signature = group.Key,
            FoundWays = group.Sum(analysis => analysis.FoundWays),
            ProducerAnalysisIds = group.SelectMany(analysis => analysis.ProducerAnalysisIds.Count > 0
                ? analysis.ProducerAnalysisIds : analysis.AnalysisId is { } id ? [id] : Array.Empty<string>()).ToArray(),
        }).ToArray();

    private static string AnalysisSignature(TraceAnalysis analysis) =>
        JsonSerializer.Serialize(new { analysis.LegacyMorphemes, analysis.Surface,
            Morphs = analysis.Morphs.Select(morph => new { morph.Identity, morph.Form, morph.Headword, morph.Gloss,
                morph.Category, morph.FormId, morph.EntryId, morph.MsaId,
                morph.InflTypeId, morph.MorphemeId, morph.AllomorphId, morph.GuessedString }) });

    private static bool IsFailure(TraceCandidate candidate) => !candidate.Succeeded &&
        candidate.OutcomeStatus != "blocked" && candidate.Steps.LastOrDefault()?.Type != "Blocked" &&
        (candidate.OutcomeStatus is "failed" or "failure" || candidate.FailureReason is { Length: > 0 } ||
         candidate.ContextualFailure is { Length: > 0 } || candidate.Steps.Any(step => step.FailureReason is { Length: > 0 }));

    private static TraceRuleReading[] Rules(TraceCandidate best)
    {
        var building = best.Steps.Where(step => step.Type.Contains("Synthesis", StringComparison.Ordinal)).ToArray();
        var path = building.Length > 0 ? building : best.Steps;
        return path
        .Where(step => !string.IsNullOrWhiteSpace(step.Source) &&
            (step.Type.Contains("Rule", StringComparison.Ordinal) || step.Type.Contains("Template", StringComparison.Ordinal)))
        .Select(step => new TraceRuleReading(step.Source!, TraceRefIds.CanonicalIdentity(step.SourceIdentityId), Kind(step.Type),
            step.Type == "Blocked" || step.OutcomeStatus == "blocked" ? "not repeated (would feed itself)"
                : step.FailureReason is { Length: > 0 } || step.OutcomeStatus is "failed" or "failure" ? "stopped"
                : step.OutcomeStatus is "successful" or "succeeded" or "success" ? "applied" : "tried",
            Explain([step], best.Steps), [step.StepId]) { RefId = step.RefId }).ToArray();
    }

    private static string Kind(string type) => type.Contains("PhonologicalRule", StringComparison.Ordinal) ? "Phonological rule"
        : type.Contains("MorphologicalRule", StringComparison.Ordinal) ? "Morphological rule"
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
            var reversed = step.Type.Contains("Analysis", StringComparison.Ordinal) && input.Length >= output.Length;
            var (before, after) = reversed ? (output, input) : (input, output);
            var prefix = after.EndsWith(before, StringComparison.Ordinal) ? after[..^before.Length] : null;
            var suffix = after.StartsWith(before, StringComparison.Ordinal) ? after[before.Length..] : null;
            var affix = step.Type.Contains("MorphologicalRule", StringComparison.Ordinal)
                ? prefix is { Length: > 0 } ? prefix + "-" : suffix is { Length: > 0 } ? "-" + suffix : null : null;
            return (affix is null ? "" : affix + " · ") + $"{before} → {after}";
        }
        return steps.Select(step => step.Output ?? step.Input).FirstOrDefault(form => !string.IsNullOrEmpty(form)) ?? "—";
    }

    internal static TraceAnalysis ToAnalysis(PanGlossTraceAnalysis analysis, int index = 0) =>
        new(analysis.AnalysisId, analysis.Index, analysis.Surface, analysis.Availability,
            analysis.Morphs.Select((morph, morphIndex) => ToMorph(morph, $"analysis:{index}:morph:{morphIndex}")).ToArray())
        {
            LegacyMorphemes = analysis.LegacyMorphemes,
            ProjectionStatus = analysis.ProjectionStatus,
            ProjectionError = analysis.ProjectionError,
            ProjectionErrorCode = analysis.ProjectionErrorCode,
        };

    internal static TraceMorph ToMorph(PanGlossTraceMorph morph, string? occurrenceId = null) =>
        new(morph.Identity, morph.Form, morph.Headword, morph.Gloss, morph.Category, morph.Slot,
            morph.InflectionClass, morph.Features, morph.GuessedString, morph.FieldWorksLink)
        {
            OccurrenceId = occurrenceId,
            FormId = TraceRefIds.CanonicalIdentity(morph.FormId),
            EntryId = TraceRefIds.CanonicalIdentity(morph.EntryId),
            MsaId = TraceRefIds.CanonicalIdentity(morph.MsaId),
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
                var stop = succeeded ? null : StoppingStep(path, id);
                var stopper = stop?.Node;
                var failure = stopper is not null
                    ? (stopper.FailureReason, stopper.FailureContext, stopper.FailureRequired,
                        stopper.FailureActual, stopper.FailureEnvironment, stopper.FailureEvidence)
                    : attempt?.FailureReason is not null
                        ? (attempt.FailureReason, attempt.FailureContext, attempt.FailureRequired,
                            attempt.FailureActual, attempt.FailureEnvironment, attempt.FailureEvidence)
                        : (node.FailureReason, node.FailureContext, node.FailureRequired,
                            node.FailureActual, node.FailureEnvironment, node.FailureEvidence);
                var reason = failure.FailureReason;
                var morphs = attempt is { Morphs.Count: > 0 }
                    ? attempt.Morphs.Select((morph, index) => ToMorph(morph, $"attempt:{id}:morph:{index}")).ToArray()
                    : MorphsReached(path, stopper).Select((morph, index) => ToMorph(morph, $"attempt:{id}:morph:{index}")).ToArray();
                candidates.Add(new TraceCandidate(
                    morphs.Select(ToReadingMorph).ToArray(),
                    succeeded,
                    reason,
                    reason is null ? null : HermitCrabFailureExplanations.Explain(reason),
                    story.ToArray())
                {
                    Surface = node.OutputShape ?? node.InputShape ?? stopper?.InputShape,
                    StoppedByRule = stopper?.Source,
                    StoppedByRuleId = TraceRefIds.CanonicalIdentity(stopper?.SourceIdentityId),
                    StoppedByRefId = stopper is null ? null : TraceRefIds.ForSource(
                        stopper.Type, stopper.Source, stopper.SourceIdentityKind, stopper.SourceIdentityId, stop?.StepId),
                    RichMorphs = morphs,
                    MorphAvailability = morphs.Length > 0 ? "recorded" : "unavailable",
                    AttemptId = attempt?.AttemptId ?? id,
                    ContextualFailure = failure.FailureContext,
                    FailureRequired = failure.FailureRequired,
                    FailureActual = failure.FailureActual,
                    FailureEnvironment = failure.FailureEnvironment,
                    FailureEvidence = failure.FailureEvidence,
                    SourceIdentityKind = attempt?.SourceIdentityKind ?? node.SourceIdentityKind,
                    SourceIdentityId = TraceRefIds.CanonicalIdentity(attempt?.SourceIdentityId ?? node.SourceIdentityId),
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

    private static (PanGlossTraceNode Node, string StepId)? StoppingStep(IReadOnlyList<PanGlossTraceNode> path, string terminalId)
    {
        for (var level = path.Count - 1; level > 0; level--)
        {
            var parent = path[level - 1];
            var index = IndexOf(parent.Children, path[level]);
            for (var sibling = index - 1; sibling >= 0; sibling--)
            {
                var candidate = parent.Children[sibling];
                if (candidate.FailureReason is { Length: > 0 } && candidate.Type is not ("Failed" or "Successful"))
                    return (candidate, string.Join(".", terminalId.Split('.').Take(level)) + "." + sibling);
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
        morph.FieldWorksLink)
    {
        AllomorphId = morph.FormId,
        GrammaticalInfoId = morph.MsaId,
    };
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
            FailureEvidence = node.FailureEvidence,
            AttemptedMorphs = node.AttemptedMorphs.Select((morph, index) => ToMorph(morph, $"step:{id}:morph:{index}")).ToArray(),
            SourceIdentityKind = node.SourceIdentityKind,
            SourceIdentityId = TraceRefIds.CanonicalIdentity(node.SourceIdentityId),
            SourceIdentityQuality = node.SourceIdentityQuality,
        };

}
