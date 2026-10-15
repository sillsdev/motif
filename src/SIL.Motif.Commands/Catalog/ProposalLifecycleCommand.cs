using System.Text.Json;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
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
        var jobs = new JobRepository(database);
        IReadOnlyList<JobRecord> trials;
        try
        {
            using var connection = database.OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            // The write lock is held, so no Trial can be enqueued between this read and the delete.
            trials = Trials.JobsFor(database, project, record.ProposalId);
            if (trials.Any(job => !JobStateMachine.IsTerminal(job.Status)))
                return Refuse<ProposalDeletedResponse>("proposal.trial-running", id,
                    "This Proposal is being tried. Wait for the try to finish, then delete it.");
            foreach (var job in trials) jobs.DeleteArchived(connection, transaction, job.JobId);
            Review.ReviewMarks.ClearAll(connection, transaction, id);
            ProposalDeletion.Delete(connection, transaction, record.ProposalId);
            transaction.Commit();
        }
        catch (ProposalRetainedByExampleSetException exception)
        {
            return Refuse<ProposalDeletedResponse>("proposal.retained-by-example-set", id, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Refuse<ProposalDeletedResponse>("proposal.applied-final", id, exception.Message);
        }
        return CommandOutcome<ProposalDeletedResponse>.Success(new ProposalDeletedResponse(id, trials.Count));
    }

    /// <summary>Declines a Proposal, finalizing it first when it is a Draft, and keeps it with its Trials.</summary>
    public static CommandOutcome<ProposalStatusChangedResponse> Decline(DeclineProposalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Close(request.ProjectPath, request.ProposalId, ManifestStatus.Rejected);
    }

    /// <summary>Withdraws a Proposal, finalizing a nonempty Draft and discarding an empty one first.</summary>
    public static CommandOutcome<ProposalStatusChangedResponse> Withdraw(WithdrawProposalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Close(request.ProjectPath, request.ProposalId, ManifestStatus.Withdrawn);
    }

    /// <summary>
    /// Opens a declined or withdrawn Proposal again as a Draft under the same id, its history kept. An applied
    /// Proposal is final and a superseded one names its replacement instead.
    /// </summary>
    public static CommandOutcome<ReopenedResponse> Reopen(ReopenProposalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var version = MotifProductVersion.CurrentText;
        var read = ReadRecord(request.ProjectPath, request.ProposalId);
        if (!read.Succeeded) return CommandOutcome<ReopenedResponse>.Refused(read.Refusal!);
        var record = read.Value!;
        var id = record.ProposalId.Value;
        if (record.Status == ManifestStatus.Applied)
            return Refuse<ReopenedResponse>("proposal.applied-final", id,
                "This Proposal was applied. An applied Proposal is final.");
        if (record.DraftName is not null ||
            record.Status is not (ManifestStatus.Rejected or ManifestStatus.Withdrawn or ManifestStatus.Superseded))
            return Refuse<ReopenedResponse>("proposal.not-closed", id,
                "Only a declined or withdrawn Proposal can be reopened.");
        return ProposalCommands.Reopen(new ReopenRequest(request.ProjectPath, version, ReviewDraftPrefix + id, id));
    }

    /// <summary>The name a Proposal reopened for review takes while it is a Draft.</summary>
    internal const string ReviewDraftPrefix = "review/";

    private static CommandOutcome<ProposalStatusChangedResponse> Close(string projectPath, string proposalId,
        string status)
    {
        var version = MotifProductVersion.CurrentText;
        var read = ReadRecord(projectPath, proposalId);
        if (!read.Succeeded) return CommandOutcome<ProposalStatusChangedResponse>.Refused(read.Refusal!);
        var record = read.Value!;
        var id = record.ProposalId.Value;
        if (record.IsPendingChanges)
            return Refuse<ProposalStatusChangedResponse>("proposal.pending-changes", id,
                "These are your own changes. Undo them on Review changes instead.");
        if (record.DraftName is { } draftName)
        {
            if (status == ManifestStatus.Withdrawn && IsNeverFinalizedEmptyDraft(record))
            {
                var discarded = ProposalCommands.DiscardDraft(new DiscardDraftRequest(projectPath, version, draftName,
                    DraftRevision.Compute(record.ProposalJson)));
                if (!discarded.Succeeded) return CommandOutcome<ProposalStatusChangedResponse>.Refused(discarded.Refusal!);
                return CommandOutcome<ProposalStatusChangedResponse>.Success(
                    new ProposalStatusChangedResponse(id, ManifestStatus.Withdrawn, null));
            }

            var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, version, draftName));
            if (!finalized.Succeeded) return CommandOutcome<ProposalStatusChangedResponse>.Refused(finalized.Refusal!);
        }
        var rejectRequest = new RejectRequest(projectPath, version, id);
        return status == ManifestStatus.Rejected
            ? ProposalCommands.Reject(rejectRequest)
            : ProposalCommands.Withdraw(rejectRequest);
    }

    private static bool IsNeverFinalizedEmptyDraft(ProposalRecord record)
    {
        if (record.IntentDigest is not null || record.ProposalJson is null) return false;
        try
        {
            return ProposalCommands.DeserializeDraft(record.ProposalJson).Operations.Count == 0;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static CommandOutcome<ProposalRecord> ReadRecord(string projectPath, string proposalId) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            try
            {
                return CommandOutcome<ProposalRecord>.Success(new ProposalRepository(database)
                    .Get(CanonicalId.Parse(ProposalCommands.NormalizeId(proposalId))));
            }
            catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
            {
                return CommandOutcome<ProposalRecord>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
            }
        });

    private static CommandOutcome<T> Refuse<T>(string code, string proposalId, string message) where T : class =>
        CommandOutcome<T>.Refused(new Refusal(code, FailureReason.Refused, message,
            new Dictionary<string, string> { ["proposalId"] = proposalId }));
}
