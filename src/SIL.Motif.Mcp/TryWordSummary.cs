using System.Text.Json.Nodes;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Mcp;

/// <summary>
/// The concise answer to "what does the parser say about this word": whether it parsed, the readings, and why not
/// when it did not. It is bounded by the word, never by the project: writing-system and style tables, the saved
/// diagnostic and the full attempt trees stay in <c>detail=detailed</c>.
/// </summary>
internal static class TryWordSummary
{
    internal const int MaxReadings = 5;
    internal const int MaxStopGroups = 5;
    internal const int MaxClosestAttempts = 3;
    internal const int MaxReasons = 5;

    private static TraceRef? RefFor(WordTraceReading reading, string? refId) =>
        refId is null ? null : reading.Refs.FirstOrDefault(r => r.Id == refId);

    // The FieldWorks name first, the parser's own id after it as a secondary detail.
    private static string? Named(TraceRef? reference, string? rule) =>
        reference?.CapturedFieldWorksLabel is { Length: > 0 } name && rule is { Length: > 0 } && name != rule
            ? $"{name} ({rule})" : rule;

    public static JsonObject Concise(WordTraceResponse response)
    {
        var reading = response.Reading;
        var result = new JsonObject
        {
            ["word"] = response.Word,
            ["parsed"] = response.Parsed,
            ["complete"] = response.Complete,
            ["searchStatus"] = response.SearchStatus,
            ["elapsedMs"] = response.ElapsedMs,
            ["attempts"] = response.ParserSteps,
            ["workSteps"] = response.TotalParserWorkSteps,
        };
        if (response.LowercaseFailure is { } lowercaseFailure) result["lowercaseFailure"] = lowercaseFailure;
        if (response.Lowercase is { } lowercase)
        {
            // Its own wordform: the readings above stay those of the word as written.
            var lowerSummary = Concise(lowercase);
            lowerSummary.Remove("more");
            result["lowercase"] = lowerSummary;
        }
        if (response.StopReason is { Length: > 0 }) result["stopReason"] = response.StopReason;
        if (response.InvalidShape) result["invalidShape"] = true;
        if (response.Guessed) result["guessed"] = true;
        if (response.DeepestRule is { Length: > 0 })
            result["deepestRule"] = Named(reading.Refs.Where(r => r.Label == response.DeepestRule).ToArray() is [var only] ? only : null, response.DeepestRule);

        var readings = new JsonArray();
        foreach (var analysis in reading.Analyses.DistinctBy(a => a.Signature ?? a.LegacyMorphemes).Take(MaxReadings))
            readings.Add(new JsonObject
            {
                ["reading"] = analysis.Signature ?? analysis.LegacyMorphemes,
                ["morphs"] = new JsonArray(analysis.Morphs.Select(m => (JsonNode)new JsonObject
                {
                    ["form"] = m.Form,
                    ["gloss"] = m.Gloss,
                    ["category"] = m.Category,
                }).ToArray()),
            });
        var readingCount = reading.Analyses.DistinctBy(a => a.Signature ?? a.LegacyMorphemes).Count();
        result["readingCount"] = readingCount;
        result["readings"] = readings;
        if (readingCount > MaxReadings)
            result["readingsNote"] = $"Showing {MaxReadings} of {readingCount} readings; pass detail=detailed for all.";

        if (reading.NoParseReasons.Count > 0)
            result["whyNot"] = new JsonArray(reading.NoParseReasons.Take(MaxReasons).Select(r => (JsonNode)r).ToArray());

        if (!response.Parsed)
        {
            var stops = new JsonArray();
            foreach (var group in reading.StopGroups.Take(MaxStopGroups))
                stops.Add(new JsonObject
                {
                    ["rule"] = Named(RefFor(reading, group.RuleRefId), group.Rule),
                    ["reason"] = group.ReasonCode,
                    ["explanation"] = group.Explanation,
                    ["attempts"] = group.Count,
                });
            result["stoppedBy"] = stops;
            if (reading.StopGroups.Count > MaxStopGroups)
                result["stoppedByNote"] = $"Showing {MaxStopGroups} of {reading.StopGroups.Count} stopping rules.";

            var closest = new JsonArray();
            foreach (var attempt in reading.ClosestAttempts.Take(MaxClosestAttempts))
                closest.Add(new JsonObject
                {
                    ["surface"] = attempt.Surface,
                    ["morphs"] = string.Join(" ", attempt.Morphs.Select(m => m.Form)),
                    ["failure"] = attempt.FailureReason,
                    ["explanation"] = attempt.Explanation,
                    ["stoppedBy"] = Named(RefFor(reading, attempt.StoppedByRefId), attempt.StoppedByRule),
                });
            result["closestAttempts"] = closest;
        }

        result["more"] = "Concise answer. Pass detail=detailed for the full trace: attempt trees, effort, "
            + "writing systems, provenance and the saved diagnostic.";
        return result;
    }
}
