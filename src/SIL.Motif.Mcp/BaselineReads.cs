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
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (baseline is null)
                return Refused("baseline.missing", FailureReason.NotFound,
                    "Motif has no saved copy of this project to read yet.");
            if (!File.Exists(baseline.FwDataPath))
                return Refused("baseline.unavailable", FailureReason.StoreInconsistent,
                    "Motif's saved copy of this project is missing from disk.");
            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            return CommandOutcome<JsonObject>.Success(read(reader.Cache));
        });

    private static CommandOutcome<JsonObject> Refused(string code, FailureReason reason, string message) =>
        CommandOutcome<JsonObject>.Refused(new Refusal(code, reason, message));
}
