using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    /// <summary>
    /// The operations of the Draft named <paramref name="draftOrProposal"/>, or, when no Draft has that name, of the
    /// finalized Proposal with that id, for a read that shows what they would change.
    /// </summary>
    internal static CommandOutcome<IReadOnlyList<OperationEnvelope>> OperationsOf(MotifDatabase database, string draftOrProposal)
    {
        var repository = new ProposalRepository(database);
        try
        {
            if (repository.DraftNameExists(draftOrProposal))
            {
                if (!TryLoadDraft(repository, draftOrProposal, out var draft, out string _))
                    return CommandOutcome<IReadOnlyList<OperationEnvelope>>.Refused(DraftNotFound(draftOrProposal));
                return CommandOutcome<IReadOnlyList<OperationEnvelope>>.Success(
                    ProposalJsonParser.Parse(BuildProposalJson(draft)).Operations);
            }
            if (CanonicalId.TryParse(draftOrProposal, out var id))
                return CommandOutcome<IReadOnlyList<OperationEnvelope>>.Success(repository.GetFinalized(id).Envelope.Operations);
        }
        catch (KeyNotFoundException)
        {
        }
        return CommandOutcome<IReadOnlyList<OperationEnvelope>>.Refused(new Refusal("draft.not-found", FailureReason.NotFound,
            $"No Draft or Proposal is named '{draftOrProposal}'. Run 'motif proposals --include-set-aside' to see Proposals.", Fact(("draftName", draftOrProposal))));
    }
}
