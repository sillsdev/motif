using System.Text.Json;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reads PanGloss's <c>generate</c> result into one <see cref="FormsExplainContext"/>: one surface per allomorph choice
/// that reached it, sorted so the same grammar always prints the same answer. It adds nothing PanGloss did not say.
/// </summary>
internal static class FormsExplainProjection
{
    internal static bool TryProject(string json, IReadOnlyList<ResolvedMorpheme> morphemes,
        out FormsExplainContext? context, out string error)
    {
        context = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException("the answer is not an object");
            var complete = Required(root, "complete").ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new FormatException("'complete' is not a Boolean"),
            };
            var stop = Text(root, "stopReason");
            if (complete && stop is not null) throw new FormatException("a complete answer carries a stopReason");
            if (!complete) stop ??= "unreported";
            var surfaces = new List<FormsExplainSurface>();
            foreach (var seed in Required(root, "seeds").EnumerateArray())
            {
                var key = Text(seed, "seedKey") ?? throw new FormatException("a seed has no seedKey");
                var allomorphs = Required(seed, "allomorphs").EnumerateArray().Select(Allomorph).ToArray();
                var truncated = seed.TryGetProperty("traceTruncated", out var cut) && cut.ValueKind == JsonValueKind.True;
                foreach (var surface in Required(seed, "surfaces").EnumerateArray())
                {
                    var steps = Steps(surface, "steps");
                    var unresolved = surface.TryGetProperty("stepsUnresolved", out var flag) && flag.ValueKind == JsonValueKind.True;
                    var alternatives = surface.TryGetProperty("alternatives", out var others) && others.ValueKind == JsonValueKind.Array
                        ? others.EnumerateArray().Select(alternative => (IReadOnlyList<FormsExplainStep>)
                            (alternative.ValueKind == JsonValueKind.Array ? alternative.EnumerateArray().Select(Step).ToArray()
                                : throw new FormatException("an alternative is not a step list"))).ToArray()
                        : [];
                    surfaces.Add(new FormsExplainSurface(
                        Text(surface, "surface") ?? throw new FormatException("a surface has no text"), key, allomorphs,
                        surface.TryGetProperty("derivations", out var count) && count.TryGetInt32(out var derivations) ? derivations : 1,
                        steps, unresolved, alternatives, truncated));
                }
            }
            surfaces.Sort(Compare);
            context = new FormsExplainContext([.. morphemes.Select(morpheme => morpheme.ToResponse())], surfaces, complete, stop);
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static FormsExplainStep[] Steps(JsonElement element, string name) =>
        element.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(Step).ToArray() : [];

    private static FormsExplainAllomorph Allomorph(JsonElement element) => new(
        Text(element, "role"), Text(element, "shape") ?? throw new FormatException("an allomorph has no shape"),
        element.TryGetProperty("allomorphId", out var id) && id.TryGetInt32(out var number) ? number : null,
        Text(element, "entryGuid"), Text(element, "msaGuid"), Text(element, "formGuid"));

    private static FormsExplainStep Step(JsonElement element)
    {
        var rule = Required(element, "rule");
        return new FormsExplainStep(
            Text(element, "stratum") ?? throw new FormatException("a step has no stratum"),
            new FormsExplainRule(Text(rule, "kind") ?? throw new FormatException("a rule has no kind"),
                Text(rule, "name"), Text(rule, "id"), Text(rule, "guid")),
            element.TryGetProperty("subrule", out var sub) && sub.TryGetInt32(out var subrule) ? subrule : null,
            Text(element, "input") ?? throw new FormatException("a step has no input"),
            Text(element, "output") ?? throw new FormatException("a step has no output"));
    }

    private static JsonElement Required(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value : throw new FormatException($"'{name}' is missing");

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Compare(FormsExplainSurface left, FormsExplainSurface right)
    {
        var byText = string.CompareOrdinal(left.Surface, right.Surface);
        return byText != 0 ? byText : CompareSeed(left.Seed, right.Seed);
    }

    // Seed keys read r<root allomorph>/p<selection>; compare the numbers so r2 sorts before r10.
    private static int CompareSeed(string left, string right)
    {
        var a = Numbers(left);
        var b = Numbers(right);
        for (var index = 0; index < Math.Min(a.Length, b.Length); index++)
            if (a[index] != b[index]) return a[index].CompareTo(b[index]);
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(left, right);
    }

    private static long[] Numbers(string seed) =>
        seed.Split(['r', 'p', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => long.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : long.MaxValue)
            .ToArray();
}
