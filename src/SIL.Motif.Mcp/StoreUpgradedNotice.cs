using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Mcp;

/// <summary>
/// Tells an assistant, on the first result after Motif upgraded a project store, that it did: one text line with the
/// schemas, the backup and what was dropped, and a <c>storeUpgrades</c> list in the structured content. The upgrade
/// happened on this call, so no later result repeats it. It is added after the result is chosen, so a refusal
/// carries it too.
/// </summary>
internal static class StoreUpgradedNotice
{
    public static CallToolResult Apply(IReadOnlyList<StoreUpgradeNotice> notices, CallToolResult result)
    {
        if (notices.Count == 0) return result;
        if (result.StructuredContent is { } structured && JsonNode.Parse(structured.GetRawText()) is JsonObject envelope)
        {
            envelope["storeUpgrades"] = JsonSerializer.SerializeToNode(notices.Select(notice => new
            {
                storePath = notice.StorePath,
                fromSchema = notice.FromSchema,
                toSchema = notice.ToSchema,
                backupPath = notice.BackupDirectory,
                receiptPath = notice.ReceiptPath,
                dropped = notice.DroppedCounts,
                message = notice.Message,
            }));
            result.StructuredContent = JsonSerializer.SerializeToElement(envelope);
        }
        result.Content = [.. notices.Select(notice => new TextContentBlock { Text = notice.Message }), .. result.Content];
        return result;
    }
}
