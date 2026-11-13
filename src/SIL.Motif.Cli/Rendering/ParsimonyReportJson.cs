using System.Linq;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

/// <summary>
/// The <c>--json</c> shape of a Parsimony Report: each finding appears once, in <c>findings</c>, and the
/// disposition projection's rows name theirs by <c>findingId</c> rather than repeating the whole body.
/// </summary>
internal static class ParsimonyReportJson
{
    /// <summary>Serializes a Report or Report set with the repeated finding bodies replaced by ids.</summary>
    public static string Serialize<T>(T response)
    {
        var node = ProjectionJson.ToNode(response);
        if (node is JsonObject root)
        {
            Slim(root);
            if (root["reports"] is JsonArray reports)
                foreach (var report in reports.OfType<JsonObject>()) Slim(report);
        }
        return ProjectionJson.Serialize(node);
    }

    private static void Slim(JsonObject report)
    {
        if (report["dispositionProjection"]?["findings"] is not JsonArray rows) return;
        var present = (report["findings"] as JsonArray)?.OfType<JsonObject>()
            .Select(finding => (string?)finding["findingId"]).OfType<string>().ToHashSet(System.StringComparer.Ordinal)
            ?? [];
        foreach (var row in rows.OfType<JsonObject>())
        {
            if (row["finding"] is not JsonObject body || (string?)body["findingId"] is not { } id ||
                !present.Contains(id))
                continue;
            row.Remove("finding");
            row["findingId"] = id;
        }
    }
}
