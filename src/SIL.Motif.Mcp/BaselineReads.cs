using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Mcp;

/// <summary>
/// Runs one read against the project's current Baseline, the saved copy Motif's other reads use, so an agent
/// never holds the live FieldWorks project open and every read sees the same saved state.
/// </summary>
internal static class BaselineReads
{
    private static readonly BaselineReadSession SharedReaders =
        new(static path => BaselineReadCache.Open(path));

    /// <summary>Opens the current Baseline privately, runs <paramref name="read"/>, and disposes the copy.</summary>
    public static CommandOutcome<JsonObject> Read(string projectPath, Func<LcmCache, JsonObject> read) =>
        Read(projectPath, (cache, _) => read(cache), SharedReaders);

    /// <summary>Runs a read with an isolated reader session.</summary>
    internal static CommandOutcome<JsonObject> Read(
        string projectPath, Func<LcmCache, JsonObject> read, BaselineReadSession readers) =>
        Read(projectPath, (cache, _) => read(cache), readers);

    /// <summary>A lexicon page with the captured source of its entries and filtered count.</summary>
    public static CommandOutcome<JsonObject> ReadLexicon(string projectPath, LexiconFilter filter,
        int limit, int offset, bool detailed) =>
        Read(projectPath, (cache, baseline) =>
        {
            var result = LexiconReader.Read(cache, filter, limit, offset, detailed);
            result["source"] = JsonNode.Parse(ProjectionJson.Serialize(new BaselineReadSource(
                baseline.Token, baseline.SourceLastWriteUtc, baseline.PublishedUtc)));
            return result;
        }, SharedReaders);

    private static CommandOutcome<JsonObject> Read(
        string projectPath, Func<LcmCache, BaselineRecord, JsonObject> read, BaselineReadSession readers) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var projectKey = ProjectWorkspaceKey.Compute(project);
            var baseline = new BaselineRepository(database).GetCurrent(projectKey);
            if (baseline is null)
                return Refused("baseline.missing", FailureReason.NotFound,
                    "Motif has no saved copy of this project to read yet.");
            if (!File.Exists(baseline.FwDataPath))
                return Refused("baseline.unavailable", FailureReason.StoreInconsistent,
                    "Motif's saved copy of this project is missing from disk.");

            return readers.Read(projectKey, baseline.Token, baseline.FwDataPath,
                cache => CommandOutcome<JsonObject>.Success(read(cache, baseline)));
        });

    static BaselineReads() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SharedReaders.Dispose();

    private static CommandOutcome<JsonObject> Refused(string code, FailureReason reason, string message) =>
        CommandOutcome<JsonObject>.Refused(new Refusal(code, reason, message));
}

/// <summary>Owns one reusable Baseline read copy and replaces it when its project identity changes.</summary>
internal sealed class BaselineReadSession : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<string, BaselineReadCache> _openReader;
    private OpenCopy? _open;

    /// <summary>Creates a reader session with its own cache-opening policy.</summary>
    internal BaselineReadSession(Func<string, BaselineReadCache> openReader) =>
        _openReader = openReader ?? throw new ArgumentNullException(nameof(openReader));

    /// <summary>Uses the matching open copy, replacing it when project or Baseline identity differs.</summary>
    internal TResult Read<TResult>(string projectKey, BaselineToken baselineToken, string fwDataPath,
        Func<LcmCache, TResult> read)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        ArgumentNullException.ThrowIfNull(baselineToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);
        ArgumentNullException.ThrowIfNull(read);

        var identity = new ReadIdentity(projectKey, baselineToken, Path.GetFullPath(fwDataPath));
        lock (_gate)
        {
            if (_open is null || !SameIdentity(_open.Identity, identity))
            {
                var previous = _open;
                _open = null;
                previous?.Reader.Dispose();
                _open = new OpenCopy(identity, _openReader(identity.Path));
            }

            return read(_open.Reader.Cache);
        }
    }

    /// <summary>Releases the open copy so another read opens a fresh Baseline cache.</summary>
    internal void Release()
    {
        lock (_gate)
        {
            var previous = _open;
            _open = null;
            previous?.Reader.Dispose();
        }
    }

    /// <summary>Releases the session's current private copy.</summary>
    public void Dispose() => Release();

    private sealed record OpenCopy(ReadIdentity Identity, BaselineReadCache Reader);

    private static bool SameIdentity(ReadIdentity left, ReadIdentity right)
    {
        var pathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(left.ProjectKey, right.ProjectKey, StringComparison.Ordinal) &&
            left.BaselineToken == right.BaselineToken &&
            string.Equals(left.Path, right.Path, pathComparison);
    }

    private sealed record ReadIdentity(string ProjectKey, BaselineToken BaselineToken, string Path);
}
