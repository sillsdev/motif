using System.Text.Json;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// Reads back what Apply saved for a Proposal: the Receipt's observed effects and the Difference summaries that were
/// kept when the Proposal was decided. Nothing here is recomputed, so the record stays true after the project moves on.
/// </summary>
internal static class ProposalAppliedRecord
{
    /// <summary>The record for an applied Proposal, or null when it has no Receipt (a legacy or hand-set status).</summary>
    internal static AppliedRecordView? Read(MotifDatabase database, string proposalId)
    {
        string? json;
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ReceiptJson FROM Receipts WHERE ProposalId = $id;";
            command.Parameters.AddWithValue("$id", proposalId);
            json = command.ExecuteScalar() as string;
        }
        if (json is null) return null;
        AppliedRecordView? view;
        try { view = ReadReceipt(json); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
        if (view is null) return null;

        var differences = new ProposalDifferenceRepository(database);
        var evidence = differences.ListFor(proposalId).Items
            .Select(saved => differences.GetSummary(saved.DifferenceId))
            .OfType<ProposalDifferenceSummary>()
            .Select(summary => new AppliedEvidenceView(summary.DifferenceId, summary.TextId, summary.TextTitle,
                summary.Words, summary.Occurrences, summary.ImprovedWords, summary.CouldBeBadWords, summary.CreatedUtc))
            .ToArray();
        return view with { Evidence = evidence };
    }

    // The Receipt is read as JSON, not as a Receipt: its ids are written as objects the typed read cannot restore.
    private static AppliedRecordView ReadReceipt(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var entry = root.GetProperty("AppliedLogEntry");
        var effects = root.GetProperty("ActualEffects").EnumerateArray().Select(effect => new AppliedEffectView(
            effect.GetProperty("CanonicalId").GetProperty("Value").GetString()!, effect.GetProperty("Field").GetString()!,
            Alternatives(effect.GetProperty("Before")), Alternatives(effect.GetProperty("After")))).ToArray();
        return new AppliedRecordView(entry.GetProperty("TimestampUtc").GetString()!,
            entry.TryGetProperty("Mode", out var mode) ? mode.GetString()! : SIL.Motif.Contract.Requests.ApplyModes.Person,
            entry.GetProperty("Description").GetString()!, entry.GetProperty("User").GetString()!,
            root.GetProperty("IntentDigest").GetString()!, effects, []);
    }

    private static Dictionary<string, string> Alternatives(JsonElement element) =>
        element.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString() ?? "", StringComparer.Ordinal);
}
