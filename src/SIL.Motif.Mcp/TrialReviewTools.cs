using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Commands.Review;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Mcp;

internal static class TrialReviewTools
{
    internal static readonly JsonObject OutputSchema = CreateOutputSchema();

    private static readonly (string Member, string Name)[] ReadFields =
    [
        (nameof(ReviewReadRequest.ProposalId), "proposal"),
        (nameof(ReviewReadRequest.JobId), "job"),
        (nameof(ReviewReadRequest.ReportId), "report"),
        (nameof(ReviewReadRequest.Latest), "latest"),
        (nameof(ReviewReadRequest.Detail), "detail"),
        (nameof(ReviewReadRequest.Section), "section"),
        (nameof(ReviewReadRequest.Set), "set"),
        (nameof(ReviewReadRequest.Outcome), "outcome"),
        (nameof(ReviewReadRequest.Flag), "flag"),
        (nameof(ReviewReadRequest.Filter), "filter"),
        (nameof(ReviewReadRequest.Word), "word"),
        (nameof(ReviewReadRequest.Case), "case"),
        (nameof(ReviewReadRequest.Trace), "trace"),
        (nameof(ReviewReadRequest.Side), "side"),
        (nameof(ReviewReadRequest.Cursor), "cursor"),
        (nameof(ReviewReadRequest.Limit), "limit"),
        (nameof(ReviewReadRequest.Page), "page"),
    ];

    internal static IEnumerable<KeyValuePair<string, string?>> ReadFieldNames =>
        ReadFields.Select(argument => new KeyValuePair<string, string?>(argument.Member, argument.Name));

    internal static IReadOnlyList<(string Name, JsonObject Schema, bool Required)> ReadArgumentSchema()
    {
        var fields = ReadFields.Select(field =>
            CatalogTools.RequestArgument<ReviewReadRequest>(field.Member, field.Name, ReviewJson.Options)).ToArray();
        var limit = fields.Single(field => field.Name == "limit").Schema;
        limit["minimum"] = ReviewReadRequest.MinimumPageSize;
        limit["maximum"] = ReviewReadRequest.MaximumPageSize;
        var page = fields.Single(field => field.Name == "page").Schema;
        page["description"] = "Page of the retirement signature rows of a saved report, filled to a byte bound. Default 1; see losses.retirementAttestation.pageCount.";
        page["minimum"] = 1;
        page["maximum"] = 1000000;
        return fields;
    }

    internal static bool HasReadSelectors(ToolArgs args) =>
        args.Has("latest") || args.Has("section") || args.Has("set") || args.Has("outcome") ||
        args.Has("flag") || args.Has("filter") || args.Has("word") || args.Has("case") || args.Has("trace") ||
        args.Has("side") || args.Has("cursor") || args.Has("limit") || args.Has("page") || args.Optional("detail") is not (null or "summary");

    internal static ToolOutcome Read(ServerContext context, string proposal, ToolArgs args)
    {
        if (ValidateInputs(args, refresh: false) is { } invalid) return invalid;
        var parsed = ParseReadRequest(proposal, args);
        if (!parsed.Succeeded) return ToolOutcome.Refused(parsed.Refusal!);
        var request = parsed.Value!;
        if (request.Trace) return ReadTrace(context, request);
        return Render(TrialReviewReads.ReadStored(context.ProjectPath, request, context.JobParserPath));
    }

    internal static ToolOutcome Refresh(ServerContext context, string proposal, ToolArgs args)
    {
        if (ValidateInputs(args, refresh: true) is { } invalid) return invalid;
        var parsed = ParseReadRequest(proposal, args);
        if (!parsed.Succeeded) return ToolOutcome.Refused(parsed.Refusal!);
        var request = parsed.Value!;
        var outcome = ProjectStoreCommand.Run<TrialReviewResult>(context.ProjectPath, context.ProductVersion,
            (database, project) => new TrialReviewCommands(database, context.JobParserPath).ProduceAsync(new ReviewProduceRequest(
                ProjectWorkspaceKey.Compute(project), proposal, request.JobId, request.ReportId)).GetAwaiter().GetResult());
        return Render(outcome);
    }

    private static ToolOutcome ReadTrace(ServerContext context, ReviewReadRequest request)
    {
        // The trace is retained in the store, so a store that is not current has nothing to trace.
        if (ProjectStoreCommand.RefuseUnlessStoreIsCurrent(context.ProjectPath, _ => TrialReviewReads.Absent(request)) is { } refused)
            return Render(refused);
        var outcome = ProjectStoreCommand.Run<TrialReviewResult>(context.ProjectPath, context.ProductVersion,
            (database, project) => new TrialReviewCommands(database, context.JobParserPath).Read(
                request with { ProjectKey = ProjectWorkspaceKey.Compute(project) }));
        return Render(outcome);
    }

    private static ToolOutcome Render(CommandOutcome<TrialReviewResult> outcome) => outcome.Succeeded
        ? ToolOutcome.Ok(JsonSerializer.SerializeToNode<TrialReviewResult>(outcome.Value!, ReviewJson.Options)!)
        : ToolOutcome.Refused(outcome.Refusal!);

    private static ToolOutcome? ValidateInputs(ToolArgs args, bool refresh)
    {
        if (args.Has("words") || args.Has("wordsFile") || args.Has("negative_words") ||
            args.Has("review_sets") || args.Has("expect_loss") || args.Has("wait_seconds") || args.Has("wait_terminal"))
            return ToolOutcome.Refused("tool.invalid-argument", FailureReason.InvalidArgument, "Stored report reads cannot supply Trial inputs.");
        var job = args.Optional("job");
        var report = args.Optional("report");
        if (job is not null && report is not null)
            return ToolOutcome.Refused("tool.invalid-argument", FailureReason.InvalidArgument, "Choose a job or a report, not both.");
        if (refresh && job is null && report is null)
            return ToolOutcome.Refused("tool.invalid-argument", FailureReason.InvalidArgument, "Refresh requires a job or report.");
        if (refresh && HasReadSelectors(args))
            return ToolOutcome.Refused("tool.invalid-argument", FailureReason.InvalidArgument, "Refresh cannot use detail selectors.");
        return null;
    }

    private static CommandOutcome<ReviewReadRequest> ParseReadRequest(string proposal, ToolArgs args)
    {
        ReviewFlagId? flag = null;
        if (args.Optional("flag") is { } flagText)
        {
            try { flag = JsonSerializer.Deserialize<ReviewFlagId>(JsonSerializer.Serialize(flagText), ReviewJson.Options); }
            catch (JsonException)
            {
                return CommandOutcome<ReviewReadRequest>.Refused(new Refusal("tool.invalid-argument",
                    FailureReason.InvalidArgument, "Unknown report flag."));
            }
        }
        Side? side = args.Optional("side") switch { null => null, "before" => Side.Before, "after" => Side.After, _ => (Side)(-1) };
        DetailLevel? detail = args.Optional("detail") switch
        { null => null, "summary" => DetailLevel.Summary, "failures" => DetailLevel.Failures, "full" => DetailLevel.Full, _ => (DetailLevel)(-1) };
        return CommandOutcome<ReviewReadRequest>.Success(new ReviewReadRequest("project", proposal,
            args.Optional("job"), args.Optional("report"), detail, args.Optional("section"), args.Optional("set"),
            Outcome: args.Optional("outcome"), Flag: flag, Filter: args.Optional("filter"), Word: args.Optional("word"),
            Case: args.Optional("case"), Trace: args.Flag("trace"), Side: side, Cursor: args.Optional("cursor"),
            Limit: args.OptionalInt("limit"), Page: args.OptionalInt("page"), Latest: args.Flag("latest")));
    }

    internal static ToolOutcome ReadJob(ServerContext context, string proposal, string job)
    {
        var outcome = TrialReviewReads.ReadJob(context.ProjectPath, context.ProductVersion, proposal, job, context.JobParserPath);
        return outcome.Succeeded ? ToolOutcome.Ok(JsonSerializer.SerializeToNode<TrialReviewResult>(outcome.Value!, ReviewJson.Options)!, jobId: job)
            : ToolOutcome.Refused(outcome.Refusal!);
    }

    internal static ToolOutcome Difference(ServerContext context, ToolArgs args) =>
        Read(context, args.Required("proposal"), args);

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
