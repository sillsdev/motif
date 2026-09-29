using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

public sealed partial class FakeCommandClient
{
    private Func<WordReadStateRequest, CancellationToken, Task<CommandOutcome<WordReadStateResponse>>>
        _readWordState = (_, _) => throw NotConfigured(nameof(ReadWordStateAsync));

    public List<WordReadStateRequest> ReadWordStateRequests { get; } = [];

    public void OnReadWordState(
        Func<WordReadStateRequest, CancellationToken, Task<CommandOutcome<WordReadStateResponse>>> behavior) =>
        _readWordState = behavior;

    public void ReadWordStateCompletesWith(WordReadStateResponse response) =>
        OnReadWordState((_, _) => Completed(response));

    public Task<CommandOutcome<WordReadStateResponse>> ReadWordStateAsync(
        WordReadStateRequest request, CancellationToken cancellationToken)
    {
        ReadWordStateRequests.Add(request);
        return _readWordState(request, cancellationToken);
    }
}
