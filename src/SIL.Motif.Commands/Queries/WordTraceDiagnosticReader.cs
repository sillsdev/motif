using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Queries;

internal static class TraceDiagnosticProjection
{
    internal static WordTraceResponse Build(
        PanGlossTraceDiagnosticDocument document, bool complete, string? stopReason, int elapsedMs)
    {
        complete = complete && document.Details.SearchCompleted;
        IReadOnlyList<TraceCandidate> candidates = document.Root is null
            ? Array.Empty<TraceCandidate>()
            : BuildCandidates(document.Root, document.Attempts);
        var root = document.Root is null
            ? new TraceStep("NoTrace", null, null, null, null, [])
            : ConvertTree(document.Root);
        var searchStatus = document.Details.InvalidShape
            ? "invalid-shape"
            : complete ? "complete" : "incomplete";
        var response = new WordTraceResponse(
            document.Word,
            candidates.Any(candidate => candidate.Succeeded) || document.Analyses.Count > 0,
            complete,
            stopReason,
            document.Root is null ? 0 : CountNodes(document.Root),
            DeriveDeepestRule(document.Root),
            elapsedMs,
            candidates,
            root)
        {
            ParserSteps = document.Details.Steps,
            ParserElapsedMs = document.Details.ElapsedNs / 1_000_000.0,
            Guessed = document.Details.Guessed,
            Effort = BuildEffort(document.Details),
            DiagnosticJson = document.RawJson,
            DiagnosticFormat = document.SchemaVersion,
            SearchStatus = searchStatus,
            InvalidShape = document.Details.InvalidShape,
            Analyses = document.Analyses.Select(ToAnalysis).ToArray(),
        };
        return WithProducerProvenance(response, document.RawJson);
    }

    private static WordTraceResponse WithProducerProvenance(WordTraceResponse response, string rawJson)
    {
        using var parsed = JsonDocument.Parse(rawJson, new JsonDocumentOptions { MaxDepth = 512 });
        var root = parsed.RootElement;
        TraceHostCapture? capture = null;
        if (root.TryGetProperty("hostCapture", out var host) && host.ValueKind == JsonValueKind.Object)
        {
            capture = host.Deserialize<TraceHostCapture>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 512 });
            if (capture is not null) capture = capture with { WritingSystems = capture.WritingSystems ?? [] };
        }
        return response with
        {
            HostCapture = capture,
            Provenance = TraceDiagnosticCapture.Compare(capture, null),
            ParserName = NestedString(root, "provenance", "parser", "name"),
            ParserVersion = NestedString(root, "provenance", "parser", "version"),
            TraceProfile = NestedString(root, "provenance", "parser", "traceProfile"),
            GrammarHash = NestedString(root, "provenance", "grammar", "grammarHash"),
            GrammarHashSemantics = NestedString(root, "provenance", "grammar", "grammarHashSemantics"),
        };
    }

    private static string? NestedString(JsonElement owner, params string[] names)
    {
        foreach (var name in names)
        {
            if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty(name, out owner))
                return null;
        }
        return owner.ValueKind == JsonValueKind.String ? owner.GetString() : null;
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
            RawJson = morph.Raw.GetRawText(),
        };

    private static IReadOnlyList<TraceEffort> BuildEffort(PanGlossTraceDetails details) =>
        details.Categories
            .Where(category => category.Attempts != 0 || category.Work != 0 || category.Outputs != 0 || category.Uses != 0 || category.NotApplied != 0 || category.NoRoot != 0 || category.SurfaceMismatch != 0 || category.SelfElapsedNs is > 0)
            .Select(category => new TraceEffort(
                KindName(category.Kind), category.Attempts, category.Outputs, category.NotApplied, category.NoRoot,
                category.SurfaceMismatch, category.Uses, category.SelfElapsedNs / 1_000_000.0)
            {
                Work = category.Work,
            })
            .ToArray();

    private static string KindName(string kind) => kind switch
    {
        "morphRule" => "Affix and derivation rules",
        "phonRule" => "Phonological rules",
        "lexEntry" => "Lexical entries",
        "rootIndex" => "Root lookups",
        "guesser" => "Guesser",
        "overlay" => "Supplied roots",
        _ => kind,
    };

    private static List<TraceCandidate> BuildCandidates(
        PanGlossTraceNode root, IReadOnlyList<PanGlossTraceAttempt> attempts)
    {
        var candidates = new List<TraceCandidate>();
        var attemptIndex = 0;
        Walk(root, [root]);
        return candidates;

        void Walk(PanGlossTraceNode node, List<PanGlossTraceNode> path)
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
                    path.Select(ConvertStep).ToArray())
                {
                    Surface = node.OutputShape ?? node.InputShape ?? stopper?.InputShape,
                    StoppedByRule = stopper?.Source,
                    StoppedByRuleId = stopper?.SourceIdentityId,
                    RichMorphs = morphs,
                    MorphAvailability = morphs.Length > 0 ? "recorded" : "unavailable",
                    AttemptId = attempt?.AttemptId,
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

            foreach (var child in node.Children)
                Walk(child, [.. path, child]);
        }
    }

    // HermitCrab records the failing rule step beside the outcome it ended, not above it: search earlier siblings.
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

    // The morphs the attempt had assembled: the outcome's, else the failing step's, else the nearest ancestor's.
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

    private static TraceStep ConvertTree(PanGlossTraceNode node) =>
        ConvertStep(node, node.Children.Select(ConvertTree).ToArray());

    private static TraceStep ConvertStep(PanGlossTraceNode node) =>
        ConvertStep(node, []);

    private static TraceStep ConvertStep(PanGlossTraceNode node, IReadOnlyList<TraceStep> children) =>
        new(node.Type, node.Source, node.InputShape, node.OutputShape, node.FailureReason, children)
        {
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

    private static int CountNodes(PanGlossTraceNode node) =>
        1 + node.Children.Sum(CountNodes);

    private static string? DeriveDeepestRule(PanGlossTraceNode? root)
    {
        if (root is null) return null;
        var bestDepth = -1;
        string? best = null;
        Walk(root, 0);
        return best;

        void Walk(PanGlossTraceNode node, int depth)
        {
            if (node.Source is not null && node.Type.Contains("Rule", StringComparison.Ordinal) && depth > bestDepth)
            {
                bestDepth = depth;
                best = node.Source;
            }
            foreach (var child in node.Children) Walk(child, depth + 1);
        }
    }
}

public static class WordTraceDiagnosticReader
{
    public static CommandOutcome<WordTraceResponse> Read(string json, int elapsedMs = 0, TraceHostCapture? current = null)
    {
        try
        {
            var document = PanGlossTraceDiagnosticReader.Read(json);
            var complete = document.Details.SearchCompleted;
            var reason = document.Details.InvalidShape
                ? "The parser could not trace this word's shape."
                : document.Details.Capped
                    ? $"The parser stopped at its step cap after {document.Details.Steps:N0} steps."
                    : document.Details.TimedOut ? "The parser stopped at its own time limit." : null;
            var response = TraceDiagnosticProjection.Build(document, complete, reason, elapsedMs);
            return CommandOutcome<WordTraceResponse>.Success(response with
            {
                Provenance = TraceDiagnosticCapture.Compare(response.HostCapture, current),
            });
        }
        catch (Exception exception) when (exception is PanGlossTraceDiagnosticFormatException or JsonException or NotSupportedException)
        {
            return CommandOutcome<WordTraceResponse>.Refused(new Refusal(
                "wordtrace.malformed-diagnostic", FailureReason.Refused, exception.Message));
        }
    }
}
