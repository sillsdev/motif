using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>A Trial of the Draft is still running, so the Draft cannot be discarded yet.</summary>
internal sealed class DraftTrialRunningException : InvalidOperationException;

/// <summary>Removes the Trial jobs of a never-finalized Draft in the same transaction that discards it.</summary>
internal static class DraftDiscardTrials
{
    /// <summary>
    /// Builds the step <see cref="ProposalRepository"/> runs before it removes a Draft's evidence: it stops while a
    /// Trial runs and otherwise deletes the Draft's finished Trial jobs, noting their ids in <paramref name="removed"/>.
    /// </summary>
    internal static Action<SqliteConnection, SqliteTransaction, CanonicalId> Remover(MotifDatabase database,
        ProjectLocator project, List<string> removed) => (connection, transaction, proposalId) =>
    {
        var trials = Trials.JobsFor(database, project, proposalId);
        if (trials.Any(job => !JobStateMachine.IsTerminal(job.Status))) throw new DraftTrialRunningException();
        var jobs = new JobRepository(database);
        foreach (var job in trials)
        {
            jobs.DeleteArchived(connection, transaction, job.JobId);
            removed.Add(job.JobId);
        }
    };
}
