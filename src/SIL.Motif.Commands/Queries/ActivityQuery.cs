using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>The one field an activity read needs: the JSON Lines log a <c>motif mcp</c> session wrote.</summary>
/// <param name="LogPath">The file named by <c>motif mcp --activity-log</c>.</param>
public sealed record ActivityRequest(string LogPath);

/// <summary>The part of one logged tool call that the summary counts.</summary>
/// <param name="Tool">The tool the agent called.</param>
/// <param name="IsError">Whether the call came back as a refusal or error.</param>
/// <param name="Code">The refusal code, or null when the call succeeded.</param>
/// <param name="FollowedNext">
/// Whether the previous call's <c>next</c> advice named this call's tool; arguments are not compared.
/// Null for a session's first call and for lines written before the field existed.
/// </param>
public sealed record ActivityCall(string Tool, bool IsError, string? Code, bool? FollowedNext);

/// <summary>What an activity log says about how the server served an agent.</summary>
/// <param name="Total">Every call.</param>
/// <param name="Errors">Calls that came back as errors.</param>
/// <param name="ByTool">Calls per tool, in tool-name order.</param>
/// <param name="RefusalsByCode">Calls per refusal code, in code order.</param>
/// <param name="FollowedNext">Calls whose tool the previous call's <c>next</c> named.</param>
/// <param name="IgnoredNext">Calls that used some other tool than the previous call's <c>next</c> named.</param>
public sealed record ActivitySummary(
    int Total, int Errors, IReadOnlyDictionary<string, int> ByTool, IReadOnlyDictionary<string, int> RefusalsByCode,
    int FollowedNext, int IgnoredNext);

/// <summary>
/// Summarises an activity log. The window-free CLI reads a file and the MCP server reads its own memory,
/// and both go through <see cref="Summarize"/> so the two renderings cannot disagree.
/// </summary>
public static class ActivityQuery
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Counts calls by tool, errors, refusals by code, and calls that followed or ignored <c>next</c>.</summary>
    public static ActivitySummary Summarize(IEnumerable<ActivityCall> calls)
    {
        var all = calls.ToList();
        return new ActivitySummary(
            all.Count,
            all.Count(call => call.IsError),
            Count(all.Select(call => call.Tool)),
            Count(all.Where(call => call.Code is not null).Select(call => call.Code!)),
            all.Count(call => call.FollowedNext == true),
            all.Count(call => call.FollowedNext == false));
    }

    /// <summary>Reads a log file; a line that is not a JSON object with a tool is skipped, as the server does.</summary>
    public static CommandOutcome<ActivitySummary> Read(ActivityRequest request)
    {
        if (!File.Exists(request.LogPath))
            return CommandOutcome<ActivitySummary>.Refused(new Refusal("activity.log-unreadable", FailureReason.NotFound,
                $"There is no activity log at '{Path.GetFileName(request.LogPath)}'. Start the server with motif mcp --activity-log <file>.",
                new Dictionary<string, string> { ["logPath"] = request.LogPath }));
        var calls = new List<ActivityCall>();
        try
        {
            foreach (var line in File.ReadLines(request.LogPath).Where(line => line.Length > 0))
            {
                try
                {
                    if (JsonSerializer.Deserialize<ActivityCall>(line, Options) is { Tool: not null } call) calls.Add(call);
                }
                catch (JsonException) { }
            }
        }
        catch (IOException exception)
        {
            return CommandOutcome<ActivitySummary>.Refused(new Refusal("activity.log-unreadable", FailureReason.NotFound,
                $"The activity log at '{Path.GetFileName(request.LogPath)}' could not be read.",
                new Dictionary<string, string> { ["logPath"] = request.LogPath, ["exceptionDetail"] = exception.Message }));
        }
        return CommandOutcome<ActivitySummary>.Success(Summarize(calls));
    }

    private static SortedDictionary<string, int> Count(IEnumerable<string> keys) =>
        new(keys.GroupBy(key => key, System.StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count()),
            System.StringComparer.Ordinal);
}
