using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    /// <summary>Records a Request: what the person asked an assistant to fix.</summary>
    Task<CommandOutcome<RequestCreatedResponse>> CreateRequestAsync(
        CreateRequestRequest request, CancellationToken cancellationToken);

    /// <summary>Lists the open and answered Requests.</summary>
    Task<CommandOutcome<RequestListResponse>> ListRequestsAsync(
        ListRequestsRequest request, CancellationToken cancellationToken);

    /// <summary>Takes an open Request back.</summary>
    Task<CommandOutcome<RequestWithdrawnResponse>> WithdrawRequestAsync(
        WithdrawRequestRequest request, CancellationToken cancellationToken);

    /// <summary>Makes a new Request from a declined Proposal's Request.</summary>
    Task<CommandOutcome<RequestCreatedResponse>> AskAgainAsync(
        AskAgainRequest request, CancellationToken cancellationToken);
}
