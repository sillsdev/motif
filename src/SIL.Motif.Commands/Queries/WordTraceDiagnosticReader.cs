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
        var reading = TraceReadingBuilder.Build(document);
        var candidates = reading.Attempts;
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
            reading)
        {
            ParserSteps = document.Details.Steps,
            ParserElapsedMs = document.Details.ElapsedNs / 1_000_000.0,
            Guessed = document.Details.Guessed,
            Effort = BuildEffort(document.Details),
            DiagnosticJson = document.RawJson,
            DiagnosticFormat = document.SchemaVersion,
            SearchStatus = searchStatus,
            InvalidShape = document.Details.InvalidShape,
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
            if (capture is not null) capture = capture with
            {
                WritingSystems = capture.WritingSystems ?? [],
                TraceLabels = capture.TraceLabels ?? [],
            };
        }
        var labels = capture?.TraceLabels.Where(label => label is not null && !string.IsNullOrEmpty(label.RefId))
            .ToLookup(label => label.RefId, StringComparer.Ordinal);
        return response with
        {
            Reading = response.Reading with
            {
                Refs = response.Reading.Refs.Select(reference => reference with
                {
                    CapturedFieldWorksLabel = labels?[reference.Id].FirstOrDefault()?.Label,
                }).ToArray(),
            },
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
