using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Mcp;

internal static class ReviewReportResources
{
    internal const string Prefix = "motif://projects/";
    internal static ListResourceTemplatesResult Templates(bool allowDrillDown = true)
    {
        var templates = new List<ResourceTemplate>
        {
            Template("Trial report", Prefix + "{projectKey}/trial-reports/{reportId}"),
            Template("Trial report section", Prefix + "{projectKey}/trial-reports/{reportId}/sections/{sectionId}{?limit,cursor}"),
        };
        if (allowDrillDown)
        {
            templates.Add(Template("Trial report case", Prefix + "{projectKey}/trial-reports/{reportId}/cases/{caseKey}"));
            templates.Add(Template("Trial report trace", Prefix + "{projectKey}/trial-reports/{reportId}/traces/{traceId}{?limit,cursor,parent}"));
        }
        return new ListResourceTemplatesResult { ResourceTemplates = templates };
    }

    internal static ReadResourceResult Read(string uri, string productVersion, string managedRoot, bool allowDrillDown = true)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Scheme != "motif" || parsed.Host != "projects")
            throw Invalid("Invalid report resource URI.");
        var parts = parsed.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (parts.Length < 3 || parts[1] != "trial-reports") throw Invalid("Invalid report resource URI.");
        if (!allowDrillDown && parts.Length > 3 && parts[3] is "cases" or "traces" or "sets")
            throw Invalid("This profile does not expose report drill-downs.");
        var projectKey = parts[0];
        var reportId = new ReviewReportId(parts[2]);
        using var machine = MachineDatabase.Open(managedRoot);
        var path = new KnownProjectRegistry(machine).List().SingleOrDefault(project => project.WorkspaceKey == projectKey)?.FullFwDataPath
            ?? throw Invalid("The report's project is not Known.");
        var query = ParseQuery(parsed.Query);
        if (parts.Length == 3 && query.Count != 0 || parts.Length == 5 && parts[3] == "cases" && query.ContainsKey("cursor"))
            throw Invalid("This resource does not accept that cursor.");
        var limit = 50;
        if (query.TryGetValue("limit", out var text) && !int.TryParse(text, out limit)) throw Invalid("Invalid report page limit.");
        if (limit is < 1 or > 200) throw Invalid("Invalid report page limit.");
        var cursor = query.GetValueOrDefault("cursor");
        var parent = query.GetValueOrDefault("parent");
        if (parent is not null && (parts.Length != 5 || parts[3] != "traces")) throw Invalid("Only traces have node children.");
        var outcome = ProjectStoreCommand.Run<object>(path, productVersion, (database, project) =>
        {
            var repository = new ReviewReportRepository(database);
            var stored = repository.ReadManifest(reportId);
            if (stored is null || stored.Summary.Pins.ProjectKey != projectKey || ProjectWorkspaceKey.Compute(project) != projectKey)
                return CommandOutcome<object>.Refused(new Refusal("difference.no-trial", FailureReason.NotFound, "The report was not found."));
            object value;
            if (parts.Length == 3)
                value = new TrialReviewCommands(database).Read(new ReviewReadRequest(projectKey, stored.Summary.Pins.ProposalId,
                    ReportId: reportId.Value)).Value!;
            else if (parts.Length == 5 && parts[3] == "sections")
                value = repository.ReadPage(new ReviewPageRequest(projectKey, reportId, parts[4], Cursor: cursor, Limit: limit));
            else if (parts.Length == 5 && parts[3] == "cases")
            {
                var read = new TrialReviewCommands(database).Read(new ReviewReadRequest(projectKey, stored.Summary.Pins.ProposalId,
                    ReportId: reportId.Value, Detail: DetailLevel.Full, Case: parts[4], Limit: limit));
                if (!read.Succeeded) return CommandOutcome<object>.Refused(read.Refusal!);
                value = ((AvailableTrialResult)read.Value!).Report.Word!;
            }
            else if (parts.Length == 6 && parts[3] == "cases")
                value = repository.ReadChildren(new ReviewChildPageRequest(projectKey, reportId, parts[5], parts[4], cursor, limit));
            else if (parts.Length == 8 && parts[3] == "sets" && parts[5] == "cells" && parts[7] == "variants")
                value = repository.ReadChildren(new ReviewChildPageRequest(projectKey, reportId, "variants", parts[6], cursor, limit) { SetId = parts[4] });
            else if (parts.Length == 5 && parts[3] == "traces")
            {
                var trace = new ReviewTraceCommands(database).Read(projectKey, reportId, parts[4], cursor, limit, parent);
                if (!trace.Succeeded) return CommandOutcome<object>.Refused(trace.Refusal!);
                value = trace.Value!;
            }
            else throw Invalid("Unknown report resource.");
            return CommandOutcome<object>.Success(value);
        });
        if (!outcome.Succeeded) throw Invalid(outcome.Refusal!.Message);
        var result = outcome.Value!;
        var json = result is TrialReviewResult trial ? JsonSerializer.Serialize(trial, ReviewJson.Options)
            : JsonSerializer.Serialize(result, result.GetType(), ReviewJson.Options);
        return new ReadResourceResult { Contents = [new TextResourceContents { Uri = uri, MimeType = "application/json", Text = json }] };
    }

    private static ResourceTemplate Template(string name, string uri) => new()
        { Name = name, UriTemplate = uri, MimeType = "application/json" };
    private static McpProtocolException Invalid(string message) => new(message, McpErrorCode.InvalidParams);
    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var term in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = term.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0]);
            if (pair.Length != 2 || key is not ("limit" or "cursor" or "parent") || !result.TryAdd(key, Uri.UnescapeDataString(pair[1])))
                throw Invalid("Invalid report resource query.");
        }
        return result;
    }
}
