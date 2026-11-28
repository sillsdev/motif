using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Reads the <c>shadowed_alternates</c> section of a <c>pangloss grammar-health --shadowed</c> report: the serve op
/// <c>shadowedAllomorphs</c>'s <c>alternates</c> (verdict <c>shadowed</c> or <c>maybe_shadowed</c>; <c>selectable</c> rows
/// are not kept) and <c>deadContinuations</c> (continuation <c>proven</c> or <c>maybe</c>; <c>live</c> rows are not kept).
/// </summary>
internal static class ShadowedAlternatesReader
{
    /// <summary>The report, or null when PanGloss wrote none because the grammar did not compile.</summary>
    internal static ShadowedAlternatesReport? Read(string reportJson)
    {
        using var document = JsonDocument.Parse(reportJson);
        if (!document.RootElement.TryGetProperty("shadowed_alternates", out var section) ||
            section.ValueKind != JsonValueKind.Object)
            return null;
        var rows = Array(section, "alternates");
        var alternates = new List<ShadowedAlternate>();
        foreach (var row in rows)
        {
            var tier = Text(row, "verdict") switch
            {
                "shadowed" => ShadowedAlternateTiers.Definite,
                "maybe_shadowed" => ShadowedAlternateTiers.Maybe,
                _ => null,
            };
            if (tier is null) continue;
            var kind = Text(row, "kind") ?? "entry";
            alternates.Add(new ShadowedAlternate(tier, kind, Owner(row), Text(row, "form"),
                Blockers(row, rows), Strings(row, "reasons"), Strings(row, "sourceAllomorphGuids"),
                Strings(row, "removableGuids"))
            { EntryGuid = Text(row, "entryGuid"), MsaGuid = Text(row, "msaGuid") });
        }
        var dead = new List<DeadContinuationAlternate>();
        foreach (var row in Array(section, "deadContinuations"))
        {
            var tier = Text(row, "continuation") switch
            {
                "proven" => ShadowedAlternateTiers.Definite,
                "maybe" => ShadowedAlternateTiers.Maybe,
                _ => null,
            };
            if (tier is null) continue;
            var blockedBy = Array(row, "blockedBy").Select(item =>
                string.Join(" ", new[] { Text(item, "kind") }.Concat(Strings(item, "names")).Where(part => !string.IsNullOrEmpty(part))))
                .Where(text => text.Length > 0).ToArray();
            dead.Add(new DeadContinuationAlternate(tier, Owner(row), Text(row, "form"), Strings(row, "reasons"),
                blockedBy, Strings(row, "sourceAllomorphGuids")));
        }
        var complete = section.TryGetProperty("complete", out var completeElement) &&
            completeElement.ValueKind == JsonValueKind.True;
        return new ShadowedAlternatesReport(complete, alternates, dead);
    }

    private static string Owner(JsonElement row) =>
        (Text(row, "kind") == "rule" ? Text(row, "ruleName") : null) ??
        Text(row, "entryId") ?? Text(row, "ruleName") ?? Text(row, "morphemeId") ?? "";

    // A blocker is named by its form when the report lists that alternate, else by its id.
    private static string[] Blockers(JsonElement row, List<JsonElement> all)
    {
        var morpheme = Text(row, "morphemeId");
        return Array(row, "blockers").Select(blocker =>
        {
            var key = Text(blocker, "xmlKey");
            var named = all.FirstOrDefault(other => Text(other, "xmlKey") == key && Text(other, "morphemeId") == morpheme);
            return named.ValueKind == JsonValueKind.Object && Text(named, "form") is { Length: > 0 } form
                ? form : key ?? "an earlier alternate";
        }).ToArray();
    }

    private static List<JsonElement> Array(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToList() : [];

    private static string[] Strings(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!).ToArray() : [];

    private static string? Text(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
