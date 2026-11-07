using System.Text.Json;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Commands.Review;

/// <summary>A cost-free standalone trace sharing the report's bounded tree projection.</summary>
public sealed record StandaloneTraceProjection(
    string Word, bool Parsed, bool Complete, string? StopReason, string SearchStatus, bool InvalidShape,
    bool Guessed, string? DeepestRule, string? LowercaseFailure, StandaloneTraceProjection? Lowercase,
    IReadOnlyList<StandaloneAnalysis> Analyses, IReadOnlyList<string> NoParseReasons,
    IReadOnlyList<StandaloneStop> StopGroups, IReadOnlyList<StandaloneAttempt> ClosestAttempts,
    IReadOnlyList<TraceRuleReading> RulesOnBestPath, IReadOnlyList<TraceRef> Refs,
    IReadOnlyList<TraceNode> Nodes, IReadOnlyList<StandaloneNodeDetail> NodeDetails, bool NodesComplete, TraceBaselineSource? Baseline,
    TraceProvenanceComparison? Provenance, string? ParserName, string? ParserVersion, string? GrammarHash,
    int? LexiconEntryCount)
{
    /// <summary>Projects only known linguistic facts; raw diagnostics and performance measurements stay private.</summary>
    public static StandaloneTraceProjection From(WordTraceResponse response)
    {
        var reading = response.Reading;
        var (nodes, complete) = ReviewTraceProjection.Capture(reading.Root);
        var details = new List<StandaloneNodeDetail>();
        var pending = new Stack<TraceStep>();
        pending.Push(reading.Root);
        while (pending.Count > 0 && details.Count < nodes.Count)
        {
            var step = pending.Pop();
            details.Add(new StandaloneNodeDetail(nodes[details.Count].Id, step.Input, step.Output,
                step.ReasonExplanation, step.FailureRequired, step.FailureActual, step.FailureEnvironment,
                step.EventEvidence?.PartialParseCause, step.EventEvidence?.LookupResult,
                Operand(step.FailureEvidence?.Payload, "requestedShape"), Operand(step.FailureEvidence?.Payload, "builtShape")));
            var count = Math.Min(step.Children.Count, ReviewTraceProjection.CaptureLimit - details.Count - pending.Count);
            for (var i = count - 1; i >= 0; i--) pending.Push(step.Children[i]);
        }
        return new(response.Word, response.Parsed, response.Complete, response.StopReason, response.SearchStatus,
            response.InvalidShape, response.Guessed, response.DeepestRule, response.LowercaseFailure,
            response.Lowercase is null ? null : From(response.Lowercase),
            reading.Analyses.Select(a => new StandaloneAnalysis(a.AnalysisId, a.Surface, a.Availability,
                a.Signature, a.LegacyMorphemes, a.Morphs.Select(Morph).ToArray())).ToArray(),
            reading.NoParseReasons,
            reading.StopGroups.Select(g => new StandaloneStop(g.Rule, g.RuleRefId, g.ReasonCode, g.Explanation)).ToArray(),
            reading.ClosestAttempts.Select(a => new StandaloneAttempt(a.Surface, a.FailureReason, a.Explanation,
                a.StoppedByRule, a.StoppedByRefId, a.RichMorphs.Count > 0 ? a.RichMorphs.Select(Morph).ToArray()
                    : a.Morphs.Select(m => new StandaloneMorph(null, null, m.Form, null, m.Gloss, m.Category,
                        null, null, null, null, m.FieldWorksLink)).ToArray())).ToArray(),
            reading.RulesOnBestPath, reading.Refs, nodes, details, complete, response.HostCapture?.Baseline,
            response.Provenance, response.ParserName, response.ParserVersion, response.GrammarHash,
            response.HostCapture?.LexiconEntryCount);
    }

    private static string? Operand(JsonElement? payload, string name) =>
        payload is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var operand) &&
        operand.ValueKind == JsonValueKind.String ? operand.GetString() : null;

    private static StandaloneMorph Morph(TraceMorph m) => new(m.Identity, m.RefId, m.Form, m.Headword,
        m.Gloss, m.Category, m.Slot, m.InflectionClass, m.Features, m.GuessedString, m.FieldWorksLink);
}

/// <summary>A recorded analysis and its linguistic morphology.</summary>
public sealed record StandaloneAnalysis(string? AnalysisId, string? Surface, string Availability,
    string? Signature, string? LegacyMorphemes, IReadOnlyList<StandaloneMorph> Morphs);
/// <summary>A morph's display and identity facts.</summary>
public sealed record StandaloneMorph(string? Identity, string? RefId, string? Form, string? Headword,
    string? Gloss, string? Category, string? Slot, string? InflectionClass, string? Features,
    string? GuessedString, string? FieldWorksLink);
/// <summary>A recorded stopping reason.</summary>
public sealed record StandaloneStop(string? Rule, string? RuleRefId, string? ReasonCode, string? Explanation);
/// <summary>A closest recorded attempt.</summary>
public sealed record StandaloneAttempt(string? Surface, string? FailureReason, string? Explanation,
    string? StoppedByRule, string? StoppedByRefId, IReadOnlyList<StandaloneMorph> Morphs);

/// <summary>Known linguistic operands and event facts associated with a bounded node.</summary>
public sealed record StandaloneNodeDetail(string Id, string? Input, string? Output, string? Explanation,
    string? Required, string? Actual, string? Environment, string? PartialParseCause, TraceLookupResult? LookupResult,
    string? RequestedShape, string? BuiltShape);
