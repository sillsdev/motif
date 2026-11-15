using System.Text.Json.Nodes;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Mcp;

internal static class ProgressToolSchema
{
    internal static JsonObject Output { get; } = Create();

    private static JsonObject Create()
    {
        using var stream = typeof(ProgressResponse).Assembly.GetManifestResourceStream("SIL.Motif.Contract.Schemas.progress.schema.json")
            ?? throw new InvalidOperationException("Progress schema is missing.");
        var contract = JsonNode.Parse(stream)!.AsObject();
        return new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = "object",
            ["$defs"] = contract["$defs"]!.DeepClone(),
            ["oneOf"] = new JsonArray(
                new JsonObject { ["type"] = "object", ["required"] = new JsonArray("ok", "result"),
                    ["properties"] = new JsonObject { ["ok"] = new JsonObject { ["const"] = true },
                        ["result"] = new JsonObject { ["$ref"] = "#/$defs/Response" },
                        ["next"] = new JsonObject { ["type"] = "string" } }, ["additionalProperties"] = false },
                new JsonObject { ["type"] = "object", ["required"] = new JsonArray("ok", "code", "reason", "message", "facts", "next"),
                    ["properties"] = new JsonObject { ["ok"] = new JsonObject { ["const"] = false },
                        ["code"] = new JsonObject { ["type"] = "string" }, ["reason"] = new JsonObject { ["type"] = "string" },
                        ["message"] = new JsonObject { ["type"] = "string" }, ["facts"] = new JsonObject { ["type"] = "object" },
                        ["next"] = new JsonObject { ["type"] = "string" } }, ["additionalProperties"] = false })
        };
    }
}
