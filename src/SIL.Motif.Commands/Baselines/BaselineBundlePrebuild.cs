using SIL.Motif.Contract.Projects;
using SIL.Motif.Host;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Baselines;

/// <summary>
/// Queues the Baseline-side Parsimony bundle right after a Capture, so it builds while the owner composes and reads.
/// </summary>
/// <remarks>
/// A Capture itself is synchronous and runs no job, so each front end calls this after a successful one and then
/// wakes the runner. The job waits behind every Trial and candidate job in the claim order, is queued once per
/// Baseline token, and is advisory: a store error leaves the Dry Run to queue it later, as before.
/// </remarks>
public static class BaselineBundlePrebuild
{
    /// <summary>Queues the prebuild for the project's current Baseline, if there is one and a parser to run it.</summary>
    /// <param name="fwDataPath">The project's saved <c>.fwdata</c> path.</param>
    /// <param name="parserPath">The PanGloss executable the job is pinned to, or null for none.</param>
    /// <returns>Whether a job was queued, so the caller knows to wake the runner.</returns>
    public static bool QueueAfterCapture(string fwDataPath, string? parserPath)
    {
        if (parserPath is null) return false;
        try
        {
            var queued = ProjectStoreCommand.Run(fwDataPath, MotifProductVersion.CurrentText, (database, project) =>
            {
                var workspaceKey = ProjectWorkspaceKey.Compute(project);
                var current = new BaselineRepository(database).GetCurrentPublished(workspaceKey)?.Token;
                var jobId = current is null
                    ? null
                    : ParsimonyAutoRun.EnqueueBaselineBundle(database, workspaceKey, current, parserPath);
                return SIL.Motif.Contract.Commands.CommandOutcome<string>.Success(jobId ?? string.Empty);
            });
            return queued.Succeeded && !string.IsNullOrEmpty(queued.Value);
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or IOException or
                                              InvalidOperationException)
        {
            return false;
        }
    }
}
