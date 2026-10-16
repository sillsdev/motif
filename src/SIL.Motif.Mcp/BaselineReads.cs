using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Commands;
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
    /// <summary>Opens the current Baseline privately, runs <paramref name="read"/>, and disposes the copy.</summary>
    public static CommandOutcome<JsonObject> Read(string projectPath, Func<LcmCache, JsonObject> read) =>
        Read(projectPath, (cache, _) => read(cache));

    /// <summary>A lexicon page with the captured source of its entries and filtered count.</summary>
    public static CommandOutcome<JsonObject> ReadLexicon(string projectPath, LexiconFilter filter,
        int limit, int offset, bool detailed) =>
        Read(projectPath, (cache, baseline) =>
        {
            var result = LexiconReader.Read(cache, filter, limit, offset, detailed);
            result["source"] = JsonNode.Parse(ProjectionJson.Serialize(new BaselineReadSource(
                baseline.Token, baseline.SourceLastWriteUtc, baseline.PublishedUtc)));
            return result;
        });

    private static CommandOutcome<JsonObject> Read(string projectPath, Func<LcmCache, BaselineRecord, JsonObject> read) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (baseline is null)
                return Refused("baseline.missing", FailureReason.NotFound,
                    "Motif has no saved copy of this project to read yet.");
            if (!File.Exists(baseline.FwDataPath))
                return Refused("baseline.unavailable", FailureReason.StoreInconsistent,
                    "Motif's saved copy of this project is missing from disk.");
            lock (OpenGate)
            {
                // A Baseline is content-addressed and read-only here, so one open copy serves every read of it.
                if (_open is null || _open.Value.Path != baseline.FwDataPath)
                {
                    _open?.Reader.Dispose();
                    _open = null;
                    _open = (baseline.FwDataPath, BaselineReadCache.Open(baseline.FwDataPath));
                }
                return CommandOutcome<JsonObject>.Success(read(_open.Value.Reader.Cache, baseline));
            }
        });

    private static readonly object OpenGate = new();
    private static (string Path, BaselineReadCache Reader)? _open;

    static BaselineReads() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { lock (OpenGate) { _open?.Reader.Dispose(); _open = null; } };

    private static CommandOutcome<JsonObject> Refused(string code, FailureReason reason, string message) =>
        CommandOutcome<JsonObject>.Refused(new Refusal(code, reason, message));
}
