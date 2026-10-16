using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parsimony;

namespace SIL.Motif.Mcp;

internal static class ParsimonyTools
{
    internal const string MeasuresResourceUri = "motif://parsimony/measures";

    internal static IReadOnlyList<AgentTool> All { get; } =
    [
        new("motif_parsimony_measures",
            string.Empty,
            AgentClass.Read,
            Schema.Object(),
            true,
            true,
            false,
            false,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ParsimonyViewsCommand.Measures(
                new ListParsimonyMeasuresRequest(c.ProjectPath, c.ProductVersion))))) { Requests = [typeof(ListParsimonyMeasuresRequest)] },

        new("motif_run_parsimony_measure",
            string.Empty,
            AgentClass.Evaluate,
            Schema.Object(
                ("measure_id", Schema.String("Exact registered measure id, or 'all' to run every supported measure " +
                    "in one job and read one Report per measure.", MeasureCatalog.All
                    .Where(measure => MeasureRunner.Supports(measure.Id)).Select(measure => measure.Id)
                    .Append(ParsimonyAdmission.AllMeasures).ToArray()), true),
                ("evidence_scope", Schema.String("Which approved-word evidence to use.",
                    "default-selection", "project-approved"), false),
                ("job", Schema.String("Job id from a previous running result; omit to start a new measure."), false),
                ("wait_seconds", Schema.Integer("How long to wait for a result. Default 40.", 1, 300), false)),
            false,
            false,
            false,
            false,
            RunMeasure) { Requests = [typeof(EnqueueParsimonyReportRequest), typeof(WaitForParsimonyReportRequest),
            typeof(WaitForParsimonyReportSetRequest)] },

        new("motif_parsimony_report",
            string.Empty,
            AgentClass.Read,
            Schema.Object(("report_id", Schema.String("Stored Parsimony Report id."), true)),
            true,
            true,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ParsimonyCommands.Show(
                new ShowParsimonyReportRequest(c.ProjectPath, c.ProductVersion, a.Required("report_id")))))) { Requests = [typeof(ShowParsimonyReportRequest)] },

        new("motif_retirement_review",
            "Reads the review state for one staged rule-based allomorph retirement Draft. It returns its latest " +
            "matching Dry Run and names evidence that is not available yet; it never invents a finding or parser result.",
            AgentClass.Read, Schema.Object(("draft_id", Schema.String("Exact staged Draft Proposal id."), true)),
            true, true, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ParsimonyCommands.ReadRetirementReview(
                new ReadRetirementReviewRequest(c.ProjectPath, c.ProductVersion, a.Required("draft_id")))))) { Requests = [typeof(ReadRetirementReviewRequest)] },

        new("motif_parsimony_view",
            string.Empty,
            AgentClass.Read,
            Schema.Object(
                ("bundle_id", Schema.String("Exact Parsimony bundle id."), true),
                ("view", Schema.String("Exact fixed view code.", ParsimonyViewCatalog.All.Select(view => view.Code).ToArray()), true),
                ("report_id", Schema.String("Stored Report id for finding views."), false),
                ("object_id", Schema.String("Exact object GUID filter where the view accepts one."), false),
                ("category_id", Schema.String("Exact category GUID filter for template-order views."), false),
                ("statement_kind", Schema.String("Statement kind for statement-usage.", "environment", "natural-class"), false),
                ("scope", Schema.String("Evidence scope for join-quality.", "project-approved", "default-selection"), false),
                ("case_key", Schema.String("Exact parser case key."), false),
                ("measure_id", Schema.String("Exact Parsimony measure id filter."), false),
                ("subject_key", Schema.String("Exact finding or judgment subject key."), false),
                ("disposition", Schema.String("Disposition filter.", "keep", "defer", "fix", "ask"), false),
                ("state", Schema.String("Current or historical finding state.", "active", "suppressed", "resurfaced",
                    "no-current-finding", "evidence-unavailable", "conflict", "unavailable", "retracted", "superseded"), false),
                ("search", Schema.String("Bounded search over returned captions and reasons."), false),
                ("limit", Schema.Integer("Rows in this page. Default 50, maximum 200; a page over about 200 KB returns fewer rows and a next cursor.", 1, 200), false),
                ("cursor", Schema.String("Next cursor from the same bundle, view and filters."), false)),
            true,
            true,
            false,
            true,
            ReadView) { Requests = [typeof(ReadParsimonyViewRequest)] },

        new("motif_revise_parsimony_disposition",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("Draft name from motif_start_proposal."), true),
                ("recordId", Schema.String("Notebook record id of a current disposition head."), true),
                ("expectedHeads", Schema.Array("Every current head with its exact content digest.",
                    Schema.Object(("revisionId", Schema.String("Portable id of the current revision."), true),
                        ("contentDigest", Schema.String("Canonical sha256 digest read with the revision."), true))), true),
                ("disposition", Schema.String("The revised choice.", "fix", "keep", "ask", "defer"), true),
                ("reason", Schema.String("Replacement reason; omit to preserve the current reason."), false),
                ("clearReason", Schema.Boolean("Clear the current optional reason."), false),
                ("question", Schema.String("Required when disposition is ask."), false)),
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ParsimonyCommands.ReviseDisposition(
                new ReviseParsimonyDispositionRequest(c.ProjectPath, c.ProductVersion, a.Required("draft"),
                    a.IntentJson("recordId", "expectedHeads", "disposition", "reason", "clearReason", "question"))),
                r => $"Draft now has {r.OperationCount} operations. Only a person can Apply this revision."))) { Requests = [typeof(ReviseParsimonyDispositionRequest)] },

        new("motif_retract_parsimony_disposition",
            string.Empty,
            AgentClass.Draft,
            Schema.Object(
                ("draft", Schema.String("Draft name from motif_start_proposal."), true),
                ("recordId", Schema.String("Notebook record id of a current disposition head."), true),
                ("expectedHeads", Schema.Array("Every current head with its exact content digest.",
                    Schema.Object(("revisionId", Schema.String("Portable id of the current revision."), true),
                        ("contentDigest", Schema.String("Canonical sha256 digest read with the revision."), true))), true)),
            false,
            false,
            false,
            true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(ParsimonyCommands.RetractDisposition(
                new RetractParsimonyDispositionRequest(c.ProjectPath, c.ProductVersion, a.Required("draft"),
                    a.IntentJson("recordId", "expectedHeads"))),
                r => $"Draft now has {r.OperationCount} operations. Only a person can Apply this retraction."))) { Requests = [typeof(RetractParsimonyDispositionRequest)] },
    ];

    internal static ListResourcesResult ListResources(bool includeMeasures) => new()
    {
        Resources = includeMeasures
            ? new[] { new Resource
                {
                    Uri = MeasuresResourceUri,
                    Name = "parsimony-measures",
                    Title = "Parsimony measures and views",
                    Description = "The fixed measure and evidence-view catalog available to this MCP profile.",
                    MimeType = "application/json",
                } }
                .Concat(EncodingGuides.Resources).ToList()
            : EncodingGuides.ListResources().Resources,
    };

    internal static ReadResourceResult ReadMeasuresResource(string projectPath, string productVersion)
    {
        var catalog = new ParsimonyMeasureCatalogResponse(MeasureCatalog.MeasureSetVersion, MeasureCatalog.All,
            ParsimonyViewCatalog.All);
        return new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = MeasuresResourceUri,
                    MimeType = "application/json",
                    Text = ProjectionJson.Serialize(catalog),
                },
            ],
        };
    }

    private static async Task<ToolOutcome> RunMeasure(ServerContext context, ToolArgs args,
        CancellationToken cancellation)
    {
        var measureId = args.Required("measure_id");
        var evidenceScope = args.Optional("evidence_scope") switch
        {
            null or "default-selection" => ParsimonyEvidenceScopeKind.DefaultSelection,
            "project-approved" => ParsimonyEvidenceScopeKind.ProjectApproved,
            _ => throw new ToolArgumentException("tool.invalid-argument",
                "evidence_scope must be 'default-selection' or 'project-approved'."),
        };
        var jobId = args.Optional("job");
        if (jobId is null)
        {
            var queued = ParsimonyCommands.Enqueue(new EnqueueParsimonyReportRequest(
                context.ProjectPath, context.ProductVersion, measureId, evidenceScope));
            if (!queued.Succeeded) return ToolOutcome.Refused(queued.Refusal!);
            jobId = queued.Value!.JobId;
        }
        context.StartRunner();

        var timeout = TimeSpan.FromSeconds(Math.Clamp(args.Int("wait_seconds", 40), 1, 300));
        // The wait polls without blocking, and a cancelled call cancels the job it started.
        if (measureId == ParsimonyAdmission.AllMeasures)
        {
            var all = await ParsimonyCommands.WaitAllAsync(new WaitForParsimonyReportSetRequest(
                context.ProjectPath, context.ProductVersion, jobId, timeout), cancellation, context.StartRunner)
                .ConfigureAwait(false);
            return all.Succeeded ? ToolOutcome.From(all) : StillRunningOrRefused(context, all.Refusal!, jobId, measureId);
        }
        var waited = await ParsimonyCommands.WaitAsync(new WaitForParsimonyReportRequest(
            context.ProjectPath, context.ProductVersion, jobId, timeout), cancellation, context.StartRunner)
                .ConfigureAwait(false);
        return waited.Succeeded ? ToolOutcome.From(waited) : StillRunningOrRefused(context, waited.Refusal!, jobId, measureId);
    }

    private static ToolOutcome StillRunningOrRefused(ServerContext context, Refusal refusal, string jobId,
        string measureId)
    {
        if (refusal.Code != "job.wait-timeout") return ToolOutcome.Refused(refusal);
        var running = new JsonObject
        {
            ["status"] = "running",
            ["jobId"] = jobId,
            ["measureId"] = measureId,
        };
        if (context.RunnerWarning is { } warning) running["runnerWarning"] = warning;
        return ToolOutcome.Ok(running, context.RunnerWarning is null
            ? "Repeat this tool with the same measure_id and the returned job id to read the Report."
            : "No runner could start, so this job will not progress: fix the runnerWarning, then repeat this tool with the same measure_id and job id.", jobId);
    }

    private static Task<ToolOutcome> ReadView(ServerContext context, ToolArgs args, CancellationToken cancellation)
    {
        var filters = new ParsimonyViewFilters(
            ObjectGuid: args.Optional("object_id"),
            CategoryGuid: args.Optional("category_id"),
            StatementKind: args.Optional("statement_kind"),
            Scope: args.Optional("scope"),
            CaseKey: args.Optional("case_key"),
            ReportId: args.Optional("report_id"),
            MeasureId: args.Optional("measure_id"),
            Disposition: args.Optional("disposition"),
            State: args.Optional("state"),
            Search: args.Optional("search"),
            SubjectKey: args.Optional("subject_key"));
        var bundleId = args.Required("bundle_id");
        var view = args.Required("view");
        var cursor = args.Optional("cursor");
        var outcome = FitToBytes(args.Int("limit", 50), ParsimonyViewLimits.McpResultBytes, limit =>
            ParsimonyViewsCommand.View(new ReadParsimonyViewRequest(context.ProjectPath, context.ProductVersion,
                new ParsimonyNamedViewRequest(bundleId, view, filters, limit, cursor))));
        return Task.FromResult(ToolOutcome.From(outcome,
            response => response.Detail?.Contains("unavailable", StringComparison.OrdinalIgnoreCase) == true
                ? "The response includes the unavailable capability or saved-judgment state. Start a Parsimony measure explicitly to request fresh evidence."
                : null));
    }

    /// <summary>
    /// Reads a page and, when its JSON exceeds the byte budget, reads again with fewer rows; the page's own cursor
    /// then continues from the first row left out. A single row over the budget is returned alone.
    /// </summary>
    internal static CommandOutcome<ParsimonyNamedViewResponse> FitToBytes(int limit, int maxBytes,
        Func<int, CommandOutcome<ParsimonyNamedViewResponse>> read)
    {
        var outcome = read(limit);
        for (var attempt = 0; attempt < 6 && outcome.Succeeded && outcome.Value!.Returned > 1; attempt++)
        {
            var bytes = System.Text.Encoding.UTF8.GetByteCount(ProjectionJson.Serialize(outcome.Value));
            if (bytes <= maxBytes) break;
            var fewer = (int)Math.Max(1L, (long)outcome.Value.Returned * maxBytes / bytes * 9 / 10);
            fewer = Math.Min(fewer, outcome.Value.Returned - 1);
            outcome = read(fewer);
        }
        return outcome;
    }
}
