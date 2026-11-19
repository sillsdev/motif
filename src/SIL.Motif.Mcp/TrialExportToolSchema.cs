using System.Text.Json.Nodes;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Mcp;

internal static class TrialExportToolSchema
{
    internal static JsonObject Output { get; } = Create();

    private static JsonObject Create()
    {
        var receipt = Load("trial-export-receipt.schema.json");
        var artifact = Load("trial-export.schema.json");
        receipt["properties"]!["page"]!["properties"]!["words"]!["items"]!["$ref"] = "#/$defs/Word";
        return new JsonObject
        {
            ["type"] = "object",
            ["$defs"] = artifact["$defs"]!.DeepClone(),
            ["oneOf"] = new JsonArray(
                Schema.Object(("ok", new JsonObject { ["const"] = true }, true),
                    ("result", receipt, true),
                    ("next", new JsonObject { ["type"] = "string" }, false)),
                Schema.Object(("ok", new JsonObject { ["const"] = false }, true),
                    ("code", new JsonObject { ["type"] = "string" }, true),
                    ("reason", new JsonObject { ["type"] = "string" }, true),
                    ("message", new JsonObject { ["type"] = "string" }, true),
                    ("facts", new JsonObject { ["type"] = "object" }, true),
                    ("next", new JsonObject { ["type"] = "string" }, true)))
        };
    }

    private static JsonObject Load(string name)
    {
        using var stream = typeof(TrialExportReceipt).Assembly.GetManifestResourceStream("SIL.Motif.Contract.Schemas." + name)
            ?? throw new InvalidOperationException("The Trial export schema is missing.");
        return JsonNode.Parse(stream)!.AsObject();
    }
}
