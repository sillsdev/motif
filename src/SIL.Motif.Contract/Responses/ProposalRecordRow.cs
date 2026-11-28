using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SIL.Motif.Contract.Responses;

/// <summary>One record file of a record import, as <c>show</c> groups the Proposal's operations by record.</summary>
/// <param name="Path">The file's path relative to the imported folder.</param>
/// <param name="Record">The canonical id of the record the file is about, when it could be read.</param>
/// <param name="Kind">The record kind the file names.</param>
/// <param name="Change"><c>create</c> or <c>update</c> for a staged file; <c>unchanged</c> or <c>refused</c> for one the import left out.</param>
/// <param name="Operations">The operations the file staged; null when the Proposal was written before the manifest counted them.</param>
/// <param name="BatchEdges">The other files of the folder this file refers to.</param>
/// <param name="HasBatchEdges">Whether <paramref name="BatchEdges"/> is above zero.</param>
/// <param name="Code">The refusal code of a refused file.</param>
public sealed record ProposalRecordRow(string Path, string? Record, string? Kind, string Change, int? Operations,
    int BatchEdges, bool HasBatchEdges, string? Code = null)
{
    /// <summary>The sort orders <c>show --records</c> accepts.</summary>
    public static readonly IReadOnlyList<string> Sorts = ["path", "kind", "batch-edges"];

    /// <summary>The rows of every record import a Proposal's extensions hold, in manifest order; null when it holds none.</summary>
    public static IReadOnlyList<ProposalRecordRow>? From(string? extensionsJson)
    {
        if (extensionsJson is null) return null;
        using var document = JsonDocument.Parse(extensionsJson);
        if (document.RootElement is not { ValueKind: JsonValueKind.Object } root ||
            !root.TryGetProperty("composers", out var composers) || composers.ValueKind != JsonValueKind.Array)
            return null;
        List<ProposalRecordRow>? rows = null;
        foreach (var provenance in composers.EnumerateArray())
        {
            if (provenance.ValueKind != JsonValueKind.Object ||
                !provenance.TryGetProperty("composer", out var name) || name.GetString() != "RecordImport" ||
                !provenance.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object ||
                !input.TryGetProperty("recordImport", out var import) || import.ValueKind != JsonValueKind.Object ||
                !import.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                continue;
            rows ??= [];
            rows.AddRange(entries.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.Object).Select(Row));
        }
        return rows;
    }

    /// <summary>The rows in the order asked for, keeping only refused ones when <paramref name="refusedOnly"/>.</summary>
    public static IReadOnlyList<ProposalRecordRow> Arrange(IEnumerable<ProposalRecordRow> rows, string sort, bool refusedOnly)
    {
        var kept = refusedOnly ? rows.Where(row => row.Change == "refused") : rows;
        return sort switch
        {
            "kind" => kept.OrderBy(row => row.Kind, System.StringComparer.Ordinal).ThenBy(row => row.Path, System.StringComparer.Ordinal).ToList(),
            "batch-edges" => kept.OrderByDescending(row => row.BatchEdges).ThenBy(row => row.Path, System.StringComparer.Ordinal).ToList(),
            _ => kept.ToList(),
        };
    }

    private static ProposalRecordRow Row(JsonElement entry)
    {
        string? Text(string property) =>
            entry.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var status = Text("status") ?? "";
        var edges = entry.TryGetProperty("dependsOn", out var depends) && depends.ValueKind == JsonValueKind.Array ? depends.GetArrayLength() : 0;
        return new ProposalRecordRow(Text("path") ?? "", Text("record"), Text("kind"),
            status switch { "created" => "create", "updated" => "update", _ => status },
            entry.TryGetProperty("operations", out var count) && count.TryGetInt32(out var number) ? number : null,
            edges, edges > 0, Text("code"));
    }
}
