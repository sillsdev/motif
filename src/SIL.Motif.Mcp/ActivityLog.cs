using System.Text.Json;
using System.Text.Json.Nodes;

namespace SIL.Motif.Mcp;

/// <summary>One tool call as the harness and <c>motif_activity</c> see it.</summary>
internal sealed record ActivityEntry(
    DateTimeOffset At, string Tool, JsonObject Args, bool IsError, string? Code, int ResultBytes, long DurationMs,
    string? JobId, string Profile);

/// <summary>
/// Remembers every tool call this server answers, in memory for <c>motif_activity</c> and, when a file is
/// named, one JSON line per call so an evaluation can measure what an agent did without trusting its account.
/// </summary>
internal sealed class ActivityLog
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private readonly string? _path;
    private readonly List<ActivityEntry> _entries = [];
    private readonly object _gate = new();

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

    public void Record(ActivityEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
            if (_path is not null) File.AppendAllText(_path, JsonSerializer.Serialize(entry, Options) + "\n");
        }
    }
}
