using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.Tests.App;

public sealed partial class FakeCommandClient
{
    private Func<ProjectStoreResetRequest, CancellationToken, Task<CommandOutcome<ProjectStoreResetResponse>>>
        _deleteRefusedStore = (_, _) => throw NotConfigured(nameof(DeleteRefusedStoreAsync));

    public List<ProjectStoreResetRequest> DeleteRefusedStoreRequests { get; } = [];

    public void OnDeleteRefusedStore(
        Func<ProjectStoreResetRequest, CancellationToken, Task<CommandOutcome<ProjectStoreResetResponse>>> behavior) =>
        _deleteRefusedStore = behavior;

    public Task<CommandOutcome<ProjectStoreResetResponse>> DeleteRefusedStoreAsync(
        ProjectStoreResetRequest request, CancellationToken cancellationToken)
    {
        DeleteRefusedStoreRequests.Add(request);
        return _deleteRefusedStore(request, cancellationToken);
    }
}
