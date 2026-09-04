using System;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    /// <summary>The refusal a failed <see cref="ProposalRepository.GetFinalized"/> load reports, shared with <see cref="JobCommands"/>.</summary>
    internal static CommandResult RefuseProposalLoad(Exception exception)
    {
        var reason = ReasonForProposal(exception);
        return new CommandResult(
            FailureEnvelope.ExitCodeFor(reason), "error: " + exception.Message + Environment.NewLine, reason);
    }
}
