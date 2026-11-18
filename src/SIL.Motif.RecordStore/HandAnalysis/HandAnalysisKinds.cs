using System.Text.Json;

namespace SIL.Motif.RecordStore.HandAnalysis;

/// <summary>Motif-owned record kinds for hand analysis; the reference projection registers them as known namespaces.</summary>
internal static class HandAnalysisKinds
{
    public const string Anchor = "hand-anchor";
    public const string Request = "hand-request";

    public static Guid? AnchorTarget(GenericRecord anchor) =>
        anchor.Values.TryGetValue("target", out var v) && v.ValueKind == JsonValueKind.String && v.TryGetGuid(out var id) &&
        anchor.Values.TryGetValue("anchor", out var text) && text.ValueKind == JsonValueKind.String ? id : null;
}
