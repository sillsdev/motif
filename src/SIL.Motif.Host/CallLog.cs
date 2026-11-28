using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host;

/// <summary>One finished call, CLI or MCP, as the daily call log records it.</summary>
/// <param name="Args">Argument name to the text of its value; the log keeps names, sizes and a hash, and values only in full mode.</param>
/// <param name="Reason">The refusal's <see cref="FailureReason"/>, or <see langword="null"/> when the call did not refuse.</param>
/// <param name="Unhandled">An exception escaped the call.</param>
/// <param name="NextNamed">The tool named in this call's <c>next</c> advice, when it gave one.</param>
/// <param name="Composers">The Composer verbs the call addressed; empty or null for every other call.</param>
/// <param name="FindComposer">Set on a Composer goal search: whether one matched and, if not, the nearest Subject.</param>
/// <param name="Outcome">Overrides the outcome class for a call whose result is not known, such as a window action.</param>
/// <param name="FollowedPrev">Whether the previous call's <c>next</c> named this call's tool; <see langword="null"/> when there was none.</param>
public sealed record CallRecord(
    string Surface, string Tool, string? Profile, IReadOnlyDictionary<string, string> Args, FailureReason? Reason,
    string? Code, bool Unhandled, int? Exit, long DurationMs, int? ResultBytes, string? JobId,
    string? NextNamed = null, bool? FollowedPrev = null, string[]? Composers = null,
    System.Text.Json.Nodes.JsonObject? FindComposer = null, string? Outcome = null);

/// <summary>
/// The daily append-only JSON Lines call log, one file per UTC day and process (<c>calls-YYYY-MM-DD-&lt;pid&gt;.jsonl</c>, so processes never overwrite each other) under <c>&lt;root&gt;/calls</c>, so a week of
/// calls can answer which tools agents used and whether they used them well. It is on by
/// default, <c>MOTIF_CALL_LOG=off</c> turns it off, a directory names another home, and <c>full</c> (or
/// <c>full:&lt;dir&gt;</c>) adds argument values. A write can never fail or slow the call it records: every IO error is swallowed.
/// </summary>
public sealed class CallLog
{
    public const string Variable = "MOTIF_CALL_LOG";
    public const string LaneVariable = "MOTIF_LANE";
    public const string AgentVariable = "MOTIF_AGENT";
    public const string SessionVariable = "MOTIF_SESSION";
    public const int SchemaVersion = 1;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string? _directory;
    private readonly bool _full;
    private readonly string _lane, _agent, _session;
    private readonly Func<DateTimeOffset> _clock;
    // One gate for the whole process: two logs in one process share a shard file, so they must not interleave.
    private static readonly object Gate = new();
    private string? _previousId;
    private bool _prepared;

    /// <summary>A log writing to <paramref name="directory"/>, or none when it is <see langword="null"/>.</summary>
    public CallLog(string? directory, bool full = false, string? lane = null, string? agent = null,
        string? session = null, Func<DateTimeOffset>? clock = null)
    {
        _directory = directory;
        _full = full;
        _lane = string.IsNullOrWhiteSpace(lane) ? "unknown" : lane;
        _agent = string.IsNullOrWhiteSpace(agent) ? "unknown" : agent;
        _session = string.IsNullOrWhiteSpace(session) ? Guid.NewGuid().ToString("N") : session;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The log the environment asks for, under <paramref name="defaultRoot"/>/calls unless it names a directory.</summary>
    public static CallLog FromEnvironment(Func<string, string?> environment, string defaultRoot)
    {
        var setting = environment(Variable)?.Trim();
        var lane = environment(LaneVariable);
        var agent = environment(AgentVariable);
        var session = environment(SessionVariable);
        if (string.Equals(setting, "off", StringComparison.OrdinalIgnoreCase))
            return new CallLog(null);
        var full = string.Equals(setting, "full", StringComparison.OrdinalIgnoreCase);
        if (setting is not null && setting.StartsWith("full:", StringComparison.OrdinalIgnoreCase))
        {
            full = true;
            setting = setting["full:".Length..];
        }
        var directory = string.IsNullOrEmpty(setting) || string.Equals(setting, "full", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(defaultRoot, "calls")
            : setting;
        return new CallLog(directory, full, lane, agent, session);
    }

    public bool Enabled => _directory is not null;

    /// <summary>The outcome class of a call: ok, user_error, refused, internal_error or transient.</summary>
    public static string Classify(FailureReason? reason, string? code, bool unhandled = false, int? exit = null)
    {
        if (unhandled) return "internal_error";
        if (reason is null)
            return exit is null or 0 or FailureEnvelope.TrialLossExitCode ? "ok" : ClassifyExit(exit.Value);
        // An unexpected exception the server turned into a refusal, and running out of memory, are Motif's own faults.
        if (code is "tool.failed" || code is not null && code.Contains("out-of-memory", StringComparison.OrdinalIgnoreCase))
            return "internal_error";
        return reason switch
        {
            FailureReason.InvalidArgument or FailureReason.NotFound => "user_error",
            FailureReason.StoreInconsistent => "internal_error",
            FailureReason.Busy or FailureReason.Cancelled or FailureReason.StoppedByBudget => "transient",
            FailureReason.Refused => "refused",
            _ => "user_error",
        };
    }

    // A CLI call that failed without a captured refusal is known only by its exit code.
    private static string ClassifyExit(int exit) => exit switch
    {
        3 or 5 => "transient",
        4 => "internal_error",
        _ => "user_error",
    };

    /// <summary>Appends one line for <paramref name="record"/>; never throws and does nothing when the log is off.</summary>
    public void Record(CallRecord record)
    {
        if (_directory is null) return;
        try
        {
            lock (Gate)
            {
                var now = _clock();
                var id = Guid.CreateVersion7(now).ToString("N");
                var line = Serialize(record, now, id, _previousId);
                _previousId = id;
                var path = Path.Combine(_directory, $"calls-{now.UtcDateTime:yyyy-MM-dd}-{Environment.ProcessId}.jsonl");
                if (!_prepared)
                {
                    Directory.CreateDirectory(_directory);
                    Prune(now);
                    _prepared = true;
                }
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(line);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Logging must never reach the call it records.
        }
    }

    private byte[] Serialize(CallRecord record, DateTimeOffset now, string id, string? parent)
    {
        var keys = record.Args.Keys.Order(StringComparer.Ordinal).ToArray();
        var sizes = keys.ToDictionary(key => key, key => Encoding.UTF8.GetByteCount(record.Args[key]));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', keys.Select(key => key + "=" + record.Args[key]))))).ToLowerInvariant()[..16];
        var line = new Line(SchemaVersion, now, id, parent, _session, _lane, _agent, record.Surface, record.Tool,
            record.Profile, keys, sizes, hash, _full ? record.Args : null,
            record.Outcome ?? Classify(record.Reason, record.Code, record.Unhandled, record.Exit), record.Code,
            record.Reason?.ToString(), record.Exit, record.DurationMs, record.ResultBytes, record.JobId,
            ProposalId(record.Args), record.NextNamed, record.FollowedPrev,
            record.Composers is { Length: > 0 } ? record.Composers : null, record.FindComposer);
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(line, Options) + "\n");
    }

    private static string? ProposalId(IReadOnlyDictionary<string, string> args) =>
        new[] { "proposal", "proposal_id", "proposalId" }
            .Select(key => args.GetValueOrDefault(key)).FirstOrDefault(value => !string.IsNullOrEmpty(value)) is { Length: <= 64 } id
            ? id : null;

    private void Prune(DateTimeOffset now)
    {
        var cutoff = now.UtcDateTime.Date - Retention;
        foreach (var file in Directory.EnumerateFiles(_directory!, "calls-*.jsonl"))
        {
            var stem = Path.GetFileNameWithoutExtension(file)["calls-".Length..];
            if (DateTime.TryParseExact(stem.Length >= 10 ? stem[..10] : stem, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day) &&
                day < cutoff)
            {
                try { File.Delete(file); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private sealed record Line(
        int V, DateTimeOffset Ts, string Id, string? Parent, string Session, string Lane, string Agent, string Surface,
        string Tool, string? Profile, string[] ArgKeys, Dictionary<string, int> ArgSizes, string ArgHash,
        IReadOnlyDictionary<string, string>? Args, string Outcome, string? Code, string? Reason, int? Exit,
        long DurMs, int? ResultBytes, string? JobId, string? ProposalId, string? NextNamed, bool? FollowedPrev,
        string[]? Composers, System.Text.Json.Nodes.JsonObject? FindComposer);
}
