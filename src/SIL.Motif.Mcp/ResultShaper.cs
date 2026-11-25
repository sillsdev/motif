using System.Text.Json.Nodes;

namespace SIL.Motif.Mcp;

/// <summary>
/// Cuts a result down to what a model needs by default: null and blank fields disappear, an empty list stays
/// so "none" can be told from "not reported", and long lists are truncated with a note saying how to see the
/// rest (operations and intents are exempt: see <c>CompleteLists</c>). Detail is opt-in, so a large project does not spend a model's context on rows it did not ask for.
/// </summary>
internal static class ResultShaper
{
    public static JsonNode? Concise(JsonNode? node, int limit) => node switch
    {
        JsonObject obj => ConciseObject(obj, limit),
        JsonArray array => ConciseArray(array, limit),
        _ => node?.DeepClone(),
    };

    private static JsonNode ConciseObject(JsonObject obj, int limit)
    {
        var result = new JsonObject();
        foreach (var (key, value) in obj)
        {
            if (value is null || value is JsonValue v && v.TryGetValue<string>(out var s) && s.Length == 0) continue;
            var shaped = value is JsonArray whole && CompleteLists.Contains(key)
                ? ConciseWholeArray(whole, limit) : Concise(value, limit);
            if (shaped is JsonObject { Count: 0 }) continue;
            result[key] = shaped;
        }
        return result;
    }

    /// <summary>Lists a reader takes as complete, so concise mode never cuts them.</summary>
    private static readonly HashSet<string> CompleteLists = new(StringComparer.Ordinal) { "operations", "intents" };

    private static JsonNode ConciseWholeArray(JsonArray array, int limit)
    {
        var result = new JsonArray();
        foreach (var item in array) result.Add(Concise(item, limit));
        return result;
    }

    private static JsonNode ConciseArray(JsonArray array, int limit)
    {
        var result = new JsonArray();
        foreach (var item in array.Take(limit)) result.Add(Concise(item, limit));
        if (array.Count > limit)
            result.Add($"... {array.Count - limit} more; pass detail=detailed or a larger limit, or narrow the request.");
        return result;
    }
}
