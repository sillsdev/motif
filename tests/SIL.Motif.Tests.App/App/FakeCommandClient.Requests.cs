using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

public sealed partial class FakeCommandClient
{
    private IReadOnlyList<RequestItem> _requests = [];

    public List<CreateRequestRequest> CreateRequestRequests { get; } = [];
    public List<WithdrawRequestRequest> WithdrawRequestRequests { get; } = [];
    public List<AskAgainRequest> AskAgainRequests { get; } = [];

    /// <summary>Sets the Requests the list read returns; the default is none.</summary>
    public void ListRequestsCompletesWith(params RequestItem[] requests) => _requests = requests;

    public Task<CommandOutcome<RequestListResponse>> ListRequestsAsync(
        ListRequestsRequest request, CancellationToken cancellationToken) =>
        Completed(new RequestListResponse(_requests.Skip(request.Offset).Take(request.Limit).ToArray(),
            _requests.Count, request.Offset, _requests.Count, string.Empty));

    public Task<CommandOutcome<RequestCreatedResponse>> CreateRequestAsync(
        CreateRequestRequest request, CancellationToken cancellationToken)
    {
        CreateRequestRequests.Add(request);
        var created = new RequestItem("request/new", request.TargetKind,
            request.TargetId, request.TargetLabel, request.Note, "open", "2026-01-01T00:00:00Z", null, null);
        _requests = [.. _requests, created];
        return Completed(new RequestCreatedResponse(created));
    }

    public Task<CommandOutcome<RequestWithdrawnResponse>> WithdrawRequestAsync(
        WithdrawRequestRequest request, CancellationToken cancellationToken)
    {
        WithdrawRequestRequests.Add(request);
        _requests = [.. _requests.Where(item => item.RequestId != request.RequestId)];
        return Completed(new RequestWithdrawnResponse(request.RequestId));
    }

    public Task<CommandOutcome<RequestCreatedResponse>> AskAgainAsync(
        AskAgainRequest request, CancellationToken cancellationToken)
    {
        AskAgainRequests.Add(request);
        return Completed(new RequestCreatedResponse(new RequestItem("request/again", "word", "w", "w", null,
            "open", "2026-01-01T00:00:00Z", null, null)));
    }
}
