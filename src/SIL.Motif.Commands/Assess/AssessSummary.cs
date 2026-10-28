using System.Text.Json.Nodes;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Commands.Assess;

/// <summary>
/// The small rendering of an Assessment: counts first, then one page of words. The agent tool and the CLI's
/// <c>--summary</c> both use it, so a large word list costs a page, not the whole list.
/// </summary>
public static class AssessSummary
{
    /// <summary>The page of a just-finished run.</summary>
    public static JsonObject Page(AssessCommandResponse response, int offset, int limit, bool detailed)
    {
        var measured = response.Measurements.FirstOrDefault(m => m.Kind == "ParseTime")?.AssessmentId
            ?? response.AssessmentIds.FirstOrDefault();
        return Build(measured, response.Words, response.InvocationId, offset, limit, detailed);
    }

    /// <summary>The page of a stored Assessment's words, read back by its id.</summary>
    public static JsonObject PageStored(string assessmentId, IReadOnlyList<AssessedWord> words, int offset,
        int limit, bool detailed) =>
        Build(assessmentId, words.Select(AssessmentWordRows.FromRecorded).ToArray(), null, offset, limit, detailed);

    /// <summary>Counts recorded words using the same outcome categories as an Assessment response.</summary>
    public static AssessmentCounts Counts(IReadOnlyList<AssessedWord> words) =>
        AssessmentCounts.Of(words.Select(AssessmentWordRows.FromRecorded));

    private static JsonObject Build(string? assessmentId, IReadOnlyList<AssessmentWordResult> words,
        string? invocationId, int offset, int limit, bool detailed)
    {
        offset = Math.Max(0, offset);
        limit = Math.Max(1, limit);
        var page = words.Skip(offset).Take(limit).ToArray();
        var end = offset + page.Length;
        var result = new JsonObject
        {
            ["counts"] = ProjectionJson.ToNode(AssessmentCounts.Of(words)),
            ["assessment"] = assessmentId,
            ["showing"] = page.Length == 0 ? $"showing 0 of {words.Count}" : $"showing {offset + 1}-{end} of {words.Count}",
            ["offset"] = offset,
            ["total"] = words.Count,
            ["nextOffset"] = end < words.Count ? end : null,
        };
        if (!string.IsNullOrEmpty(invocationId)) result["invocation"] = invocationId;
        result["words"] = new JsonArray(page.Select(row => detailed ? Detailed(row) : Concise(row)).ToArray());
        return result;
    }

    private static JsonNode Concise(AssessmentWordResult row)
    {
        var node = new JsonObject { ["word"] = row.Word, ["outcome"] = row.Outcome };
        if (row.Outcome == "analysed")
        {
            node["reading"] = AssessmentCounts.IsAccepted(row) ? "accepted" : "guess-only";
            node["readings"] = row.Morphology?.Analyses.Count ?? 0;
        }
        return node;
    }

    private static JsonNode Detailed(AssessmentWordResult row) =>
        ProjectionJson.ToNode(row)!;
}
