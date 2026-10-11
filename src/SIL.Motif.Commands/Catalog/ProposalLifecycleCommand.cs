using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Catalog;

/// <summary>
/// The person's moves on a Proposal's lifecycle. Applied is final: an applied Proposal is never reopened or deleted,
/// and its Receipts are kept. A closed Proposal that never applied can be deleted with its Trials.
/// </summary>
public static class ProposalLifecycleCommand
{
    /// <summary>The statuses a Proposal is closed in without having been applied.</summary>
    public static IReadOnlySet<string> DeletableStatuses { get; } =
        new HashSet<string>(StringComparer.Ordinal) { ManifestStatus.Rejected, ManifestStatus.Superseded, "withdrawn" };

    /// <summary>Deletes a declined, superseded or withdrawn Proposal that never applied, and its Trial records.</summary>
    public static CommandOutcome<ProposalDeletedResponse> Delete(DeleteProposalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
            Delete(database, project, request.ProposalId));
    }

    /// <summary>Deletes a closed Proposal that never applied from an open store.</summary>
    public static CommandOutcome<ProposalDeletedResponse> Delete(MotifDatabase database, ProjectLocator project,
        string proposalId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        var repository = new ProposalRepository(database);
        ProposalRecord record;
        try
        {
            record = repository.Get(CanonicalId.Parse(ProposalCommands.NormalizeId(proposalId)));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            return CommandOutcome<ProposalDeletedResponse>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
        }
        var id = record.ProposalId.Value;
        if (record.Status == ManifestStatus.Applied)
            return Refuse<ProposalDeletedResponse>("proposal.applied-final", id,
                "This Proposal was applied. An applied Proposal and its Receipts are kept.");
        if (record.DraftName is not null || !DeletableStatuses.Contains(record.Status))
            return Refuse<ProposalDeletedResponse>("proposal.not-closed", id,
                "Only a declined, superseded or withdrawn Proposal can be deleted.");
        var trials = Trials.JobsFor(database, project, record.ProposalId);
        if (trials.Any(job => !JobStateMachine.IsTerminal(job.Status)))
            return Refuse<ProposalDeletedResponse>("proposal.trial-running", id,
                "This Proposal is being tried. Wait for the try to finish, then delete it.");

        var jobs = new JobRepository(database);
        foreach (var job in trials) jobs.DeleteArchived(job.JobId);
        new AssessmentRepository(database).DeleteByProposal(record.ProposalId, exceptAssessmentId: null);
        try
        {
            repository.DeleteArchived(record.ProposalId);
        }
        catch (InvalidOperationException exception)
        {
            return Refuse<ProposalDeletedResponse>("proposal.applied-final", id, exception.Message);
        }
        return CommandOutcome<ProposalDeletedResponse>.Success(new ProposalDeletedResponse(id, trials.Count));
    }

    private static CommandOutcome<T> Refuse<T>(string code, string proposalId, string message) where T : class =>
        CommandOutcome<T>.Refused(new Refusal(code, FailureReason.Refused, message,
            new Dictionary<string, string> { ["proposalId"] = proposalId }));
}
