using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Projection;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

/// <summary>
/// <c>--pick a,b.c,items[].name</c>: keeps only the named fields of a <c>--json</c> result, so a script needs no
/// JSON slicing. The output is one JSON object whose keys are the paths exactly as given.
/// </summary>
/// <remarks>
/// A path is dot-separated property names (case-sensitive, as printed). <c>name[]</c> takes every element of an array and
/// yields an array of what follows; <c>name[2]</c> takes one element. A property missing from some elements of a
/// <c>[]</c> array gives null for those elements. A path that matches nothing at all is refused with the names that
/// exist at the point it failed, never printed as null. A property name containing '.', '[' or ']' cannot be addressed. Pinned by `PickProjectionTests`.
/// </remarks>
public static class PickProjection
{
    /// <summary>The refusal for a <c>--pick</c> given without <c>--json</c>.</summary>
    public static CommandResult NeedsJson() => Refuse("--pick projects a --json result; add --json.");

    /// <summary>Projects a successful command's JSON text, or returns the refusal.</summary>
    public static CommandResult Apply(CommandResult result, string spec)
    {
        var paths = spec.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (paths.Length == 0) return Refuse("--pick needs one or more paths, for example --pick gate.state,words[].form.");
        JsonNode? root;
        try { root = JsonNode.Parse(result.Output); }
        catch (JsonException) { return Refuse("This command printed no single JSON document, so --pick cannot project it."); }

        var picked = new JsonObject();
        foreach (var path in paths)
        {
            var segments = Segments(path);
            if (segments is null) return Refuse($"The --pick path '{path}' is not valid; use names joined by dots, with [] or [n] after an array.");
            if (!TryWalk(root, segments, 0, out var value, out var failure))
                return Refuse($"The --pick path '{path}' matches nothing: {failure}");
            picked[path] = value;
        }
        return new CommandResult(0, picked.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static List<(string Name, int? Index, bool All)>? Segments(string path)
    {
        var list = new List<(string, int?, bool)>();
        foreach (var raw in path.Split('.'))
        {
            var name = raw;
            int? index = null;
            var all = false;
            var open = raw.IndexOf('[');
            if (open >= 0)
            {
                if (!raw.EndsWith(']')) return null;
                name = raw[..open];
                var inner = raw[(open + 1)..^1];
                if (inner.Length == 0) all = true;
                else if (int.TryParse(inner, out var n) && n >= 0) index = n;
                else return null;
            }
            if (name.Length == 0) return null;
            list.Add((name, index, all));
        }
        return list;
    }

    private static bool TryWalk(JsonNode? node, List<(string Name, int? Index, bool All)> segments, int at,
        out JsonNode? value, out string failure)
    {
        value = null;
        failure = "";
        if (at == segments.Count) { value = node?.DeepClone(); return true; }
        var (name, index, all) = segments[at];
        if (node is not JsonObject obj)
        {
            failure = $"'{name}' was looked up in a value that is not an object.";
            return false;
        }
        if (!obj.TryGetPropertyValue(name, out var child))
        {
            failure = $"there is no '{name}'; the names here are: {string.Join(", ", obj.Select(p => p.Key))}.";
            return false;
        }
        if (all)
        {
            if (child is not JsonArray array) { failure = $"'{name}' is not an array."; return false; }
            var results = new JsonArray();
            var matched = 0;
            string? lastFailure = null;
            foreach (var element in array)
            {
                if (TryWalk(element, segments, at + 1, out var item, out var itemFailure))
                {
                    matched++;
                    results.Add(item);
                }
                else
                {
                    lastFailure = itemFailure;
                    results.Add(null);
                }
            }
            if (array.Count > 0 && matched == 0) { failure = lastFailure!; return false; }
            value = results;
            return true;
        }
        if (index is { } position)
        {
            if (child is not JsonArray array) { failure = $"'{name}' is not an array."; return false; }
            if (position >= array.Count) { failure = $"'{name}' has {array.Count} elements, so [{position}] does not exist."; return false; }
            return TryWalk(array[position], segments, at + 1, out value, out failure);
        }
        return TryWalk(child, segments, at + 1, out value, out failure);
    }

    private static CommandResult Refuse(string message) => new(
        FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument),
        ProjectionJson.Serialize(CliNext.Envelope(FailureReason.InvalidArgument, message,
            code: HostRefusalCodes.CliPickPath)) + Environment.NewLine,
        FailureReason.InvalidArgument);
}
