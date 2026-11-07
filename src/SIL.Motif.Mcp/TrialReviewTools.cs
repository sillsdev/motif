using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Review;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Mcp;

internal static class TrialReviewTools
{
    internal static readonly JsonObject OutputSchema = CreateOutputSchema();
    internal static readonly JsonObject DifferenceSchema = CreateDifferenceSchema();

    internal static ToolOutcome Read(ServerContext context, string proposal, ToolArgs args, bool refresh = false)
    {
        if (args.Has("words") || args.Has("wordsFile") || args.Has("negative_words") ||
            args.Has("review_sets") || args.Has("wait_seconds"))
            return ToolOutcome.Refused("tool.invalid-argument", FailureReason.InvalidArgument, "Stored report reads cannot supply Trial inputs.");
        var job = args.Optional("job");
        var report = args.Optional("report");
        if (job is not null && report is not null)
            return ToolOutcome.Refused("tool.invalid-argument", FailureReason.InvalidArgument, "Choose a job or a report, not both.");
        if (refresh && job is null && report is null)
            return ToolOutcome.Refused("tool.invalid-argument", FailureReason.InvalidArgument, "Refresh requires a job or report.");
        if (refresh && (args.Has("detail") && args.Optional("detail") != "summary" || args.Has("section") ||
            args.Has("set") || args.Has("cursor") || args.Has("limit")))
            return ToolOutcome.Refused("tool.invalid-argument", FailureReason.InvalidArgument, "Refresh cannot use detail selectors.");
        ReviewFlagId? flag = null;
        if (args.Optional("flag") is { } flagText)
        {
            try { flag = JsonSerializer.Deserialize<ReviewFlagId>(JsonSerializer.Serialize(flagText), ReviewJson.Options); }
            catch (JsonException) { return ToolOutcome.Refused("tool.invalid-argument", FailureReason.InvalidArgument, "Unknown report flag."); }
        }
        Side? side = args.Optional("side") switch { null => null, "before" => Side.Before, "after" => Side.After, _ => (Side)(-1) };
        DetailLevel? detail = args.Optional("detail") switch
        { null => null, "summary" => DetailLevel.Summary, "failures" => DetailLevel.Failures, "full" => DetailLevel.Full, _ => (DetailLevel)(-1) };
        var outcome = ProjectStoreCommand.Run<TrialReviewResult>(context.ProjectPath, context.ProductVersion, (database, project) =>
        {
            var key = ProjectWorkspaceKey.Compute(project);
            var commands = new TrialReviewCommands(database);
            return refresh ? commands.ProduceAsync(new ReviewProduceRequest(key, proposal, job, report)).GetAwaiter().GetResult()
                : commands.Read(new ReviewReadRequest(key, proposal, job, report, detail, args.Optional("section"),
                    args.Optional("set"), Outcome: args.Optional("outcome"), Flag: flag, Filter: args.Optional("filter"),
                    Word: args.Optional("word"), Case: args.Optional("case"), Trace: args.Flag("trace"), Side: side,
                    Cursor: args.Optional("cursor"), Limit: args.OptionalInt("limit")));
        });
        return outcome.Succeeded ? ToolOutcome.Ok(JsonSerializer.SerializeToNode<TrialReviewResult>(outcome.Value!, ReviewJson.Options)!)
            : ToolOutcome.Refused(outcome.Refusal!);
    }

    internal static ToolOutcome ReadJob(ServerContext context, string proposal, string job)
    {
        var outcome = ProjectStoreCommand.Run<TrialReviewResult>(context.ProjectPath, context.ProductVersion, (database, project) =>
        {
            var commands = new TrialReviewCommands(database);
            var key = ProjectWorkspaceKey.Compute(project);
            var read = commands.Read(new ReviewReadRequest(key, proposal, JobId: job));
            return read.Refusal?.Code == SIL.Motif.Contract.Commands.RefusalCodes.DifferenceNoBefore
                ? commands.ProduceAsync(new ReviewProduceRequest(key, proposal, job, null)).GetAwaiter().GetResult() : read;
        });
        return outcome.Succeeded ? ToolOutcome.Ok(JsonSerializer.SerializeToNode<TrialReviewResult>(outcome.Value!, ReviewJson.Options)!, jobId: job)
            : ToolOutcome.Refused(outcome.Refusal!);
    }

    internal static ToolOutcome Difference(ServerContext context, ToolArgs args)
    {
        if (!args.Has("report"))
        {
            if (new[] { "proposal", "section", "set", "outcome", "flag", "filter", "word", "case", "side", "trace", "cursor" }.Any(args.Has))
                return ToolOutcome.Refused("tool.invalid-argument", FailureReason.InvalidArgument, "Legacy Difference and report selectors are exclusive.");
            return TrialResults.Read(context, args);
        }
        if (args.Has("difference") || args.Has("category") || args.Has("offset"))
            return ToolOutcome.Refused("tool.invalid-argument", FailureReason.InvalidArgument, "Report and legacy Difference selectors are exclusive.");
        return Read(context, args.Required("proposal"), args);
    }

    private static JsonObject CreateDifferenceSchema()
    {
        var schema = OutputSchema.DeepClone().AsObject();
        var success = schema["oneOf"]![0]!.AsObject();
        success["properties"]!["result"] = new JsonObject { ["oneOf"] = new JsonArray(
            new JsonObject { ["$ref"] = "#/$defs/TrialReviewResult" },
            new JsonObject { ["type"] = "object", ["required"] = new JsonArray("difference", "category", "total", "offset", "returned", "nextOffset", "items"),
                ["properties"] = new JsonObject { ["difference"] = new JsonObject { ["type"] = "string" },
                    ["category"] = new JsonObject { ["type"] = "string" }, ["total"] = new JsonObject { ["type"] = "integer" },
                    ["offset"] = new JsonObject { ["type"] = "integer" }, ["returned"] = new JsonObject { ["type"] = "integer" },
                    ["nextOffset"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
                    ["items"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object" } } },
                ["additionalProperties"] = false }) };
        return schema;
    }

    private static JsonObject CreateOutputSchema()
    {
        using var stream = typeof(ReviewJson).Assembly.GetManifestResourceStream("SIL.Motif.Contract.Schemas.proposal-review-report.schema.json")
            ?? throw new InvalidOperationException("Review schema is missing.");
        var contract = JsonNode.Parse(stream)!.AsObject();
        return new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = "object",
            ["$defs"] = contract["$defs"]!.DeepClone(),
            ["oneOf"] = new JsonArray(
                new JsonObject { ["type"] = "object", ["required"] = new JsonArray("ok", "result"),
                    ["properties"] = new JsonObject { ["ok"] = new JsonObject { ["const"] = true },
                        ["result"] = new JsonObject { ["$ref"] = "#/$defs/TrialReviewResult" },
                        ["next"] = new JsonObject { ["type"] = "string" } }, ["additionalProperties"] = false },
                new JsonObject { ["type"] = "object", ["required"] = new JsonArray("ok", "code", "reason", "message", "facts", "next"),
                    ["properties"] = new JsonObject { ["ok"] = new JsonObject { ["const"] = false },
                        ["code"] = new JsonObject { ["type"] = "string" }, ["reason"] = new JsonObject { ["type"] = "string" },
                        ["message"] = new JsonObject { ["type"] = "string" }, ["facts"] = new JsonObject { ["type"] = "object" },
                        ["next"] = new JsonObject { ["type"] = "string" } }, ["additionalProperties"] = false })
        };
    }
}
