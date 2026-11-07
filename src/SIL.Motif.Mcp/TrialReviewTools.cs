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
        var detail = args.Optional("detail") switch
        { null or "summary" => DetailLevel.Summary, "failures" => DetailLevel.Failures, "full" => DetailLevel.Full, _ => (DetailLevel)(-1) };
        var outcome = ProjectStoreCommand.Run<TrialReviewResult>(context.ProjectPath, context.ProductVersion, (database, project) =>
        {
            var key = ProjectWorkspaceKey.Compute(project);
            var commands = new TrialReviewCommands(database);
            return refresh ? commands.ProduceAsync(new ReviewProduceRequest(key, proposal, job, report)).GetAwaiter().GetResult()
                : commands.Read(new ReviewReadRequest(key, proposal, job, report, detail, args.Optional("section"),
                    args.Optional("set"), Cursor: args.Optional("cursor"), Limit: args.OptionalInt("limit")));
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
