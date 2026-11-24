using System.Text.Json;

namespace SIL.Motif.Host.PanGloss;

/// <summary>One allomorph of the scanned morpheme, as PanGloss wrote it.</summary>
/// <param name="AllomorphId">The compiled allomorph id <c>generate</c> reports.</param>
/// <param name="Form">The allomorph's own text; an affix keeps its boundary marker.</param>
/// <param name="Segments">The form's segments, without boundaries.</param>
/// <param name="FormPartial">True when an affix process copies or modifies stem material, so <paramref name="Form"/> holds only the segments the allomorph writes.</param>
public sealed record EffectsAllomorph(int AllomorphId, string Form, IReadOnlyList<string> Segments, bool FormPartial);

/// <summary>The rule an effect names.</summary>
/// <param name="Kind"><c>rewrite</c> or <c>metathesis</c>.</param>
/// <param name="Id">The rule's id in the grammar.</param>
/// <param name="Guid">The rule's FieldWorks GUID.</param>
/// <param name="Name">The rule's name.</param>
/// <param name="Stratum">The rule's stratum, or null for a rule that is not compiled.</param>
/// <param name="Ordinal">The rule's place in its stratum, or null for a rule that is not compiled.</param>
/// <param name="Status"><c>compiled</c>, <c>not_compiled</c> or <c>disabled</c>.</param>
public sealed record EffectsRule(string Kind, string Id, string Guid, string Name, string? Stratum, int? Ordinal, string Status);

/// <summary>One allomorph, rule, subrule and edge, with what PanGloss could prove about it.</summary>
/// <param name="Rule">The rule.</param>
/// <param name="Subrule">The subrule's index, or null for a metathesis rule.</param>
/// <param name="AllomorphId">The allomorph the row is about.</param>
/// <param name="Edge"><c>left</c> or <c>right</c>.</param>
/// <param name="MayApply"><c>unknown</c> or <c>notOnForm</c>; PanGloss never says a rule cannot change a form, only that its target is not on it.</param>
/// <param name="Reason">Why: <c>no_lhs_match</c> for <c>notOnForm</c>, else <c>environment_unchecked</c>, <c>lhs_may_straddle_edge</c>, <c>may_be_fed</c> or an unmodelled reason.</param>
/// <param name="LhsSpans">The <c>[start, end)</c> segment spans of the form the rule's target matches; empty when there are none.</param>
public sealed record EffectRow(EffectsRule Rule, int? Subrule, int AllomorphId, string Edge, string MayApply, string? Reason,
    IReadOnlyList<(int Start, int End)> LhsSpans)
{
    /// <summary>The rule's target has no match on the form, across either edge, and nothing earlier can feed it.</summary>
    public bool NotOnForm => MayApply == "notOnForm";
}

/// <summary>The answer to an <c>effects</c> request.</summary>
/// <param name="Complete">False when the scan stopped, then <paramref name="Effects"/> holds the units finished so far.</param>
/// <param name="StopReason"><c>work_limit</c>, <c>deadline</c> or null.</param>
/// <param name="Partial">True exactly when the scan stopped.</param>
/// <param name="Allomorphs">The morpheme's allomorphs.</param>
/// <param name="Effects">One row per allomorph, rule, subrule and edge.</param>
public sealed record EffectsResult(bool Complete, string? StopReason, bool Partial, IReadOnlyList<EffectsAllomorph> Allomorphs,
    IReadOnlyList<EffectRow> Effects)
{
    /// <summary>The rows that may change a form: everything PanGloss could not prove the rule's target absent from.</summary>
    public IEnumerable<EffectRow> Survivors => Effects.Where(row => !row.NotOnForm);

    /// <summary>Reads the <c>result</c> line of an <c>effects</c> request; throws <see cref="FormatException"/> when it is not one.</summary>
    public static EffectsResult Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("type").GetString() != "result" || root.GetProperty("op").GetString() != "effects")
                throw new FormatException("The line is not an effects result.");
            var allomorphs = root.GetProperty("allomorphs").EnumerateArray().Select(item => new EffectsAllomorph(
                item.GetProperty("allomorphId").GetInt32(), item.GetProperty("form").GetString() ?? "",
                [.. item.GetProperty("segments").EnumerateArray().Select(segment => segment.GetString() ?? "")],
                item.GetProperty("formPartial").GetBoolean())).ToList();
            var effects = root.GetProperty("effects").EnumerateArray().Select(ReadRow).ToList();
            return new EffectsResult(root.GetProperty("complete").GetBoolean(), Text(root, "stopReason"),
                root.GetProperty("partial").GetBoolean(), allomorphs, effects);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new FormatException("The effects reply is not readable: " + exception.Message, exception);
        }
    }

    private static EffectRow ReadRow(JsonElement item)
    {
        var rule = item.GetProperty("rule");
        var spans = item.TryGetProperty("lhsSpans", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(span => (span[0].GetInt32(), span[1].GetInt32())).ToList() : [];
        return new EffectRow(
            new EffectsRule(rule.GetProperty("kind").GetString() ?? "", rule.GetProperty("id").GetString() ?? "",
                rule.GetProperty("guid").GetString() ?? "", rule.GetProperty("name").GetString() ?? "", Text(rule, "stratum"),
                rule.TryGetProperty("ordinal", out var ordinal) && ordinal.ValueKind == JsonValueKind.Number ? ordinal.GetInt32() : null,
                rule.GetProperty("status").GetString() ?? ""),
            item.TryGetProperty("subrule", out var subrule) && subrule.ValueKind == JsonValueKind.Number ? subrule.GetInt32() : null,
            item.GetProperty("allomorphId").GetInt32(), item.GetProperty("edge").GetString() ?? "",
            item.GetProperty("mayApply").GetString() ?? "", Text(item, "reason"), spans);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
