using System.Text.Json;

namespace SIL.Motif.Contract.Model;

/// <summary>
/// The composer provenance Motif's own record import writes into a Proposal, and the one question review asks of it:
/// whether the import wrote every operation. No request, verb or batch names this composer, so only the import path
/// can produce an entry for it.
/// </summary>
public static class RecordImportProvenance
{
    /// <summary>The composer name the import records its operations under.</summary>
    public const string ComposerName = "RecordImport";

    /// <summary>The record kind of a lexicon entry file; only an import of these files counts as a lexicon import.</summary>
    public const string LexiconKind = "lexicon/entry";

    /// <summary>
    /// Whether the Proposal has operations and every one of them is listed by a lexicon record-import entry; an
    /// operation the import did not write, or one an import of other record kinds wrote, makes the Proposal not wholly a
    /// lexicon import.
    /// </summary>
    public static bool WritesEveryOperation(Proposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposal.Operations.Count == 0) return false;
        if (proposal.Extensions is not { ValueKind: JsonValueKind.Object } extensions ||
            !extensions.TryGetProperty("composers", out var composers) || composers.ValueKind != JsonValueKind.Array)
            return false;
        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in composers.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("composer", out var name) || name.ValueKind != JsonValueKind.String ||
                name.GetString() != ComposerName ||
                !entry.TryGetProperty("operationIds", out var ids) || ids.ValueKind != JsonValueKind.Array ||
                !entry.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object ||
                !input.TryGetProperty("lexicon", out var lexicon) || lexicon.ValueKind != JsonValueKind.True)
                continue;
            foreach (var id in ids.EnumerateArray())
                if (id.ValueKind == JsonValueKind.String) written.Add(id.GetString()!);
        }
        return proposal.Operations.All(operation => written.Contains(operation.OperationId.Value));
    }
}
