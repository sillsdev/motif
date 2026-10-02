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
        var attempts = document.Root is null ? [] : BuildCandidates(document.Root).ToArray();
        var analyses = document.Analyses.Select((analysis, index) => ToAnalysis(analysis, index)).ToArray();
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
        var logical = LogicalAnalyses(analyses);
        return new WordTraceReading(word, root, attempts, analyses, stops, closest, rules)
        {
            Refs = Refs(root, attempts, analyses),
            LogicalAnalyses = logical,
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

    private static TraceLogicalAnalysis[] LogicalAnalyses(IReadOnlyList<TraceAnalysis> analyses) => analyses
        .Select((analysis, position) => (Analysis: analysis, Position: position))
        .GroupBy(item => MorphologyKey(item.Analysis) ?? $"occurrence:{item.Position}", StringComparer.Ordinal)
        .Select(group => new TraceLogicalAnalysis(group.First().Analysis.Signature ?? AnalysisSignature(group.First().Analysis),
            group.Select(item => item.Position).ToArray())).ToArray();

    // Rendering cannot establish equality when the producer did not project exact ordered morphology.
    private static string? MorphologyKey(TraceAnalysis analysis)
    {
        if (analysis.ProjectionStatus != "available" || analysis.Morphs.Count == 0 ||
            analysis.Morphs.Any(morph => morph.IdentityQuality != "authored" ||
                !Guid.TryParse(morph.FormId, out _) || !Guid.TryParse(morph.MsaId, out _) ||
                morph.InflTypeId is not null && !Guid.TryParse(morph.InflTypeId, out _))) return null;
        return JsonSerializer.Serialize(analysis.Morphs.Select(morph => new {
            Form = TraceRefIds.CanonicalIdentity(morph.FormId), Msa = TraceRefIds.CanonicalIdentity(morph.MsaId),
            InflType = TraceRefIds.CanonicalIdentity(morph.InflTypeId) }));
    }

    private static string AnalysisSignature(TraceAnalysis analysis) =>
        $"{analysis.LegacyMorphemes ?? string.Join(" ", analysis.Morphs.Select(morph => morph.Form ?? "?"))}|{analysis.Surface}";

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
            step.Type == "Blocked" || step.OutcomeStatus == "blocked" ? "Blocked"
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
            : HermitCrabFailureExplanations.Explain(failed.FailureReason!) ?? $"Explanation not recorded (reason code: {failed.FailureReason}).";
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
            Signature = $"{analysis.LegacyMorphemes ?? string.Join(" ", analysis.Morphs.Select(morph => morph.Form ?? "?"))}|{analysis.Surface}",
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
        PanGlossTraceNode root)
    {
        var candidates = new List<TraceCandidate>();
        Walk(root, [], [], "0");
        return candidates;

        void Walk(PanGlossTraceNode node, List<TraceStep> ancestors, List<TraceTreeContextRange> context, string id)
        {
            var step = ConvertStep(node, id);
            var path = new List<TraceStep>(ancestors) { step };
            if (node.Type is "Successful" or "Failed")
            {
                var morphs = path.AsEnumerable().Reverse().FirstOrDefault(item => item.AttemptedMorphs.Count > 0)
                    ?.AttemptedMorphs ?? [];
                var succeeded = node.Type == "Successful";
                candidates.Add(new TraceCandidate(morphs.Select(ToReadingMorph).ToArray(), succeeded,
                    node.FailureReason, node.FailureReason is null ? null : HermitCrabFailureExplanations.Explain(node.FailureReason),
                    path.ToArray())
                {
                    AttemptId = id,
                    OutcomeStatus = node.OutcomeStatus ?? (succeeded ? "successful" : "failed"),
                    Surface = node.OutputShape ?? node.InputShape,
                    RichMorphs = morphs,
                    MorphAvailability = morphs.Count > 0 ? "recorded" : "unavailable",
                    TreeContext = context.ToArray(),
                    ContextualFailure = node.FailureContext,
                    FailureRequired = node.FailureRequired,
                    FailureActual = node.FailureActual,
                    FailureEnvironment = node.FailureEnvironment,
                    FailureEvidence = node.FailureEvidence,
                    SourceIdentityKind = node.SourceIdentityKind,
                    SourceIdentityId = TraceRefIds.CanonicalIdentity(node.SourceIdentityId),
                    SourceIdentityQuality = node.SourceIdentityQuality,
                });
            }
            for (var index = 0; index < node.Children.Count; index++)
            {
                var child = node.Children[index];
                var childId = $"{id}.{index}";
                var earlier = new List<TraceTreeContextRange>(context);
                if (index > 0) earlier.Add(new TraceTreeContextRange(id, index));
                Walk(child, path, earlier, childId);
            }
        }
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

    private static TraceStep ConvertTree(PanGlossTraceNode node, string id) =>
        ConvertStep(node, id, node.Children.Select((child, index) => ConvertTree(child, $"{id}.{index}")).ToArray());

    private static TraceStep ConvertStep(PanGlossTraceNode node, string id) => ConvertStep(node, id, []);

    private static TraceStep ConvertStep(PanGlossTraceNode node, string id, IReadOnlyList<TraceStep> children) =>
        new(node.Type, node.Source, node.InputShape, node.OutputShape, node.FailureReason, children)
        {
            StepId = id,
            ReasonExplanation = node.FailureReason is null ? null : HermitCrabFailureExplanations.Explain(node.FailureReason),
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
