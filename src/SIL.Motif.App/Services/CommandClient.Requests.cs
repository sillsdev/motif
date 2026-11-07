using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public Task<CommandOutcome<RequestCreatedResponse>> CreateRequestAsync(
        CreateRequestRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => RequestCommands.Create(request), cancellationToken);

    public Task<CommandOutcome<RequestListResponse>> ListRequestsAsync(
        ListRequestsRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => RequestCommands.List(request), cancellationToken);

    public Task<CommandOutcome<RequestWithdrawnResponse>> WithdrawRequestAsync(
        WithdrawRequestRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => RequestCommands.Withdraw(request), cancellationToken);

    public Task<CommandOutcome<RequestCreatedResponse>> AskAgainAsync(
        AskAgainRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => RequestCommands.AskAgain(request), cancellationToken);
}
