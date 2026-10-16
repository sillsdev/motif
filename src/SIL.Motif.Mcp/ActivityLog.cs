using System.Text.Json;
using System.Text.Json.Nodes;

namespace SIL.Motif.Mcp;

/// <summary>One tool call as the harness and <c>motif_activity</c> see it.</summary>
/// <param name="FindComposer">
/// Set only on a <c>motif_find_composer</c> goal search: whether a composer matched and, if not, the nearest Subject.
/// </param>
/// <param name="Composers">
/// The Composer verbs the call addressed: the verb of a composer tool, or the distinct step verbs of a batch.
/// Empty for every other tool and for lines written before the field existed.
/// </param>
/// <param name="FollowedNext">
/// <see langword="null"/> for the first call of a server's life; otherwise whether the previous call's
/// <c>next</c> advice, success or refusal, named this call's tool; arguments are not compared.
/// </param>
internal sealed record ActivityEntry(
    DateTimeOffset At, string Tool, JsonObject Args, bool IsError, string? Code, int ResultBytes, long DurationMs,
    string? JobId, string Profile, JsonObject? FindComposer = null, string[]? Composers = null,
    bool? FollowedNext = null);

/// <summary>
/// Remembers every tool call this server answers, in memory for <c>motif_activity</c> and, when a file is
/// named, one JSON line per call so an evaluation can measure what an agent did without trusting its account.
/// </summary>
internal sealed partial class ActivityLog
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private readonly string? _path;
    private readonly List<ActivityEntry> _entries = [];
    private readonly object _gate = new();
    private bool _answeredCall;
    private string? _previousNext;

    public ActivityLog(string? path)
    {
        _path = path;
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadLines(path).Where(line => line.Length > 0))
        {
            try { _entries.Add(JsonSerializer.Deserialize<ActivityEntry>(line, Options)!); }
            catch (JsonException) { }
        }
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
            if (_path is not null) File.AppendAllText(_path, JsonSerializer.Serialize(entry, Options) + "\n");
        }
    }

    private static bool NamesTool(string? next, string tool) =>
        next is not null && ToolToken().Matches(next).Any(match => match.Value == tool);

    [System.Text.RegularExpressions.GeneratedRegex("motif_[A-Za-z0-9_]*[A-Za-z0-9]")]
    private static partial System.Text.RegularExpressions.Regex ToolToken();
}
