using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Cli.Rendering;

/// <summary>
/// Tells the caller of a verb that Motif upgraded a project store on the way: one line on standard error, and under
/// <c>--json</c> an <c>upgrade</c> object in the result, so an agent reading only the result still learns it.
/// </summary>
internal static class StoreUpgradeReport
{
    public static CommandResult Apply(IReadOnlyList<StoreUpgradeNotice> notices, CommandResult result, bool asJson, TextWriter error)
    {
        if (notices.Count == 0) return result;
        foreach (var notice in notices) error.WriteLine(Line(notice));
        if (!asJson || result.ExitCode != 0 || JsonNodeOrNull(result.Output) is not JsonObject envelope) return result;
        var described = notices.Select(Describe).ToArray();
        envelope[notices.Count == 1 ? "upgrade" : "upgrades"] = notices.Count == 1 ? described[0] : new JsonArray(described);
        return result with { Output = envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine };
    }

    /// <summary>The standard-error line for one upgrade.</summary>
    internal static string Line(StoreUpgradeNotice notice) =>
        $"Upgraded store schema {notice.FromSchema} -> {notice.ToSchema}; backup: {notice.BackupDirectory ?? "none"}; " +
        "dropped: " + (notice.DroppedCounts.Any(pair => pair.Value > 0)
            ? string.Join(", ", notice.DroppedCounts.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key} ({pair.Value})"))
            : "none");

    private static JsonObject Describe(StoreUpgradeNotice notice) => new()
    {
        ["storePath"] = notice.StorePath,
        ["fromSchema"] = notice.FromSchema,
        ["toSchema"] = notice.ToSchema,
        ["backupPath"] = notice.BackupDirectory,
        ["receiptPath"] = notice.ReceiptPath,
        ["dropped"] = JsonSerializer.SerializeToNode(notice.DroppedCounts),
        ["message"] = notice.Message,
    };

    private static JsonNode? JsonNodeOrNull(string text)
    {
        try { return JsonNode.Parse(text); }
        catch (JsonException) { return null; }
    }
}
