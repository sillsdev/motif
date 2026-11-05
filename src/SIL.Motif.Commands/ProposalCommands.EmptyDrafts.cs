using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    /// <summary>
    /// Discards every Draft that was never finalized and holds no operation, which is what a failed compose leaves
    /// behind after <see cref="New"/>. Pending changes and finalized or reopened Drafts are never touched.
    /// A Draft with Rationale, Comments or retained evidence stays and is reported with the reason.
    /// </summary>
    public static CommandOutcome<EmptyDraftsDiscardedResponse> DiscardEmptyDrafts(DiscardEmptyDraftsRequest request)
    {
        var listed = ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
            CommandOutcome<IReadOnlyList<(string Name, CanonicalId Id, string Revision)>>.Success(new ProposalRepository(database)
                .ListDrafts()
                .Where(draft => draft.DraftName is not null && !draft.IsPendingChanges &&
                    (request.NamePrefix is null || draft.DraftName.StartsWith(request.NamePrefix, StringComparison.Ordinal)) &&
                    ProposalLifecycleCommand.IsNeverFinalizedEmptyDraft(draft))
                .Select(draft => (draft.DraftName!, draft.ProposalId, DraftRevision.Compute(draft.ProposalJson)))
                .ToList()));
        if (!listed.Succeeded) return CommandOutcome<EmptyDraftsDiscardedResponse>.Refused(listed.Refusal!);
        var discarded = new List<string>();
        var kept = new List<EmptyDraftKept>();
        foreach (var (name, id, revision) in listed.Value!)
        {
            var outcome = DiscardDraft(new DiscardDraftRequest(request.FwDataPath, request.ProductVersion, name, revision), id);
            if (outcome.Succeeded) discarded.Add(name);
            else kept.Add(new EmptyDraftKept(name, outcome.Refusal!.Code, outcome.Refusal.Message));
        }
        return CommandOutcome<EmptyDraftsDiscardedResponse>.Success(new EmptyDraftsDiscardedResponse(discarded, kept));
    }
}
