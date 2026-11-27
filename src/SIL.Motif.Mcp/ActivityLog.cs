using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.Mcp;

/// <summary>One tool call as the harness and <c>motif_activity</c> see it.</summary>
/// <param name="FindComposer">
/// Set only on a <c>motif_find_composer</c> goal search: whether a composer matched and, if not, the nearest Subject.
/// </param>
/// <param name="Composers">
/// The Composer verbs the call addressed: the verb of a composer tool, or the distinct step verbs of a batch.
/// Empty for every other tool and for lines written before the field existed.
/// </param>
/// <param name="Reason">The refusal's reason, for the call log's outcome class; <see langword="null"/> when the call succeeded.</param>
/// <param name="FollowedNext">
/// <see langword="null"/> for the first call of a server's life; otherwise whether the previous call's
/// <c>next</c> advice, success or refusal, named this call's tool; arguments are not compared.
/// </param>
internal sealed record ActivityEntry(
    DateTimeOffset At, string Tool, JsonObject Args, bool IsError, string? Code, int ResultBytes, long DurationMs,
    string? JobId, string Profile, JsonObject? FindComposer = null, string[]? Composers = null,
    bool? FollowedNext = null, FailureReason? Reason = null);

/// <summary>
/// Remembers every tool call this server answers, in memory for <c>motif_activity</c>, and hands each one to the
/// daily call log so an evaluation can measure what an agent did without trusting its account.
/// </summary>
internal sealed partial class ActivityLog
{
    private readonly List<ActivityEntry> _entries = [];
    private readonly object _gate = new();
    private bool _answeredCall;
    private string? _previousNext;
    private readonly CallLog? _calls;

    /// <param name="calls">The daily call log every call is also written to, or <see langword="null"/> for none.</param>
    public ActivityLog(CallLog? calls = null)
    {
        _calls = calls;
    }

    public IReadOnlyList<ActivityEntry> Entries
    {
        get { lock (_gate) return _entries.ToList(); }
    }

    /// <summary>Records one call and judges it against the <c>next</c> advice the previous call gave.</summary>
    public void Record(ActivityEntry entry, string? next = null)
    {
        lock (_gate)
        {
            entry = entry with
            {
                FollowedNext = _answeredCall ? NamesTool(_previousNext, entry.Tool) : null,
                Composers = entry.Composers ?? [],
            };
            _answeredCall = true;
            _previousNext = next;
            _entries.Add(entry);
            var unregistered = entry.Code == "tool.unknown";
            _calls?.Record(new CallRecord("mcp", unregistered ? "unknown" : entry.Tool, entry.Profile,
                unregistered ? new Dictionary<string, string>() : ArgumentTexts(entry.Args), entry.Reason,
                entry.Code, false, null, entry.DurationMs, entry.ResultBytes, entry.JobId,
                next is null ? null : ToolToken().Match(next) is { Success: true } named ? named.Value : null,
                entry.FollowedNext, entry.Composers, entry.FindComposer));
        }
    }

    private static Dictionary<string, string> ArgumentTexts(JsonObject args) =>
        args.ToDictionary(pair => pair.Key, pair => pair.Value is JsonValue { } value && value.TryGetValue<string>(out var text)
            ? text : pair.Value?.ToJsonString() ?? "null");

    private static bool NamesTool(string? next, string tool) =>
        next is not null && ToolToken().Matches(next).Any(match => match.Value == tool);

    [System.Text.RegularExpressions.GeneratedRegex("motif_[A-Za-z0-9_]*[A-Za-z0-9]")]
    private static partial System.Text.RegularExpressions.Regex ToolToken();
}
