using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Tests.App;

public sealed partial class FakeCommandClient
{
    public IReadOnlyList<SourceSummary> SourceRows { get; set; } = [];
    public Func<SourceListRequest, CancellationToken, Task<CommandOutcome<SourceListResponse>>>? ListSourcesHandler { get; set; }
    public List<SourceListRequest> ListSourcesRequests { get; } = [];
    public Task<CommandOutcome<SourceListResponse>> ListSourcesAsync(
        SourceListRequest request, CancellationToken cancellationToken)
    {
        ListSourcesRequests.Add(request);
        return ListSourcesHandler?.Invoke(request, cancellationToken) ?? Completed(new SourceListResponse(SourceRows));
    }

    public Func<SourceShowRequest, CancellationToken, Task<CommandOutcome<SourceShowResponse>>>? SourceShowHandler { get; set; }
    public List<SourceShowRequest> ShowSourceRequests { get; } = [];
    public Task<CommandOutcome<SourceShowResponse>> ShowSourceAsync(
        SourceShowRequest request, CancellationToken cancellationToken)
    {
        ShowSourceRequests.Add(request);
        return SourceShowHandler?.Invoke(request, cancellationToken) ?? Completed(new SourceShowResponse(SourceRows.Single(row => row.Id == request.Id), 3));
    }

    public Func<SourceWhereRequest, CancellationToken, Task<CommandOutcome<SourceWhereResponse>>>? WhereSourceHandler { get; set; }
    public List<SourceWhereRequest> WhereSourceRequests { get; } = [];
    public Task<CommandOutcome<SourceWhereResponse>> WhereSourceAsync(
        SourceWhereRequest request, CancellationToken cancellationToken)
    {
        WhereSourceRequests.Add(request);
        return WhereSourceHandler?.Invoke(request, cancellationToken) ?? Completed(new SourceWhereResponse(request.Ref, [new(request.Source!, "InContext", null)]));
    }

    public Func<CurrentGetRequest, CancellationToken, Task<CommandOutcome<CurrentGetResponse>>>? GetSourceCurrentHandler { get; set; }
    public List<CurrentGetRequest> GetSourceCurrentRequests { get; } = [];
    public Task<CommandOutcome<CurrentGetResponse>> GetSourceCurrentAsync(
        CurrentGetRequest request, CancellationToken cancellationToken)
    {
        GetSourceCurrentRequests.Add(request);
        return GetSourceCurrentHandler?.Invoke(request, cancellationToken) ?? Completed(new CurrentGetResponse([new(request.Source!, "MAT 4", "Current")]));
    }

    public Func<SourceSetFolderRequest, CancellationToken, Task<CommandOutcome<SourceLocalResponse>>>? SetSourceFolderHandler { get; set; }
    public List<SourceSetFolderRequest> SetSourceFolderRequests { get; } = [];
    public Task<CommandOutcome<SourceLocalResponse>> SetSourceFolderAsync(
        SourceSetFolderRequest request, CancellationToken cancellationToken)
    {
        SetSourceFolderRequests.Add(request);
        return SetSourceFolderHandler?.Invoke(request, cancellationToken) ?? Completed(new SourceLocalResponse(request.Source, null, null));
    }

    public Func<SourceClearFolderRequest, CancellationToken, Task<CommandOutcome<SourceLocalResponse>>>? ClearSourceFolderHandler { get; set; }
    public List<SourceClearFolderRequest> ClearSourceFolderRequests { get; } = [];
    public Task<CommandOutcome<SourceLocalResponse>> ClearSourceFolderAsync(
        SourceClearFolderRequest request, CancellationToken cancellationToken)
    {
        ClearSourceFolderRequests.Add(request);
        return ClearSourceFolderHandler?.Invoke(request, cancellationToken) ?? Completed(new SourceLocalResponse(request.Source, null, null));
    }

    public Func<CurrentSetRequest, CancellationToken, Task<CommandOutcome<SourceLocalResponse>>>? SetSourceCurrentHandler { get; set; }
    public List<CurrentSetRequest> SetSourceCurrentRequests { get; } = [];
    public Task<CommandOutcome<SourceLocalResponse>> SetSourceCurrentAsync(
        CurrentSetRequest request, CancellationToken cancellationToken)
    {
        SetSourceCurrentRequests.Add(request);
        return SetSourceCurrentHandler?.Invoke(request, cancellationToken) ?? Completed(new SourceLocalResponse(request.Source, null, null));
    }

    public Func<CurrentClearRequest, CancellationToken, Task<CommandOutcome<SourceLocalResponse>>>? ClearSourceCurrentHandler { get; set; }
    public List<CurrentClearRequest> ClearSourceCurrentRequests { get; } = [];
    public Task<CommandOutcome<SourceLocalResponse>> ClearSourceCurrentAsync(
        CurrentClearRequest request, CancellationToken cancellationToken)
    {
        ClearSourceCurrentRequests.Add(request);
        return ClearSourceCurrentHandler?.Invoke(request, cancellationToken) ?? Completed(new SourceLocalResponse(request.Source, null, null));
    }

    public Func<SourceAddRequest, CancellationToken, Task<CommandOutcome<SourceStageResponse>>>? StageSourceAddHandler { get; set; }
    public List<SourceAddRequest> StageSourceAddRequests { get; } = [];
    public Task<CommandOutcome<SourceStageResponse>> StageSourceAddAsync(
        SourceAddRequest request, CancellationToken cancellationToken)
    {
        StageSourceAddRequests.Add(request);
        return StageSourceAddHandler?.Invoke(request, cancellationToken) ?? Refused<SourceStageResponse>(new Refusal("source.unconfigured", FailureReason.Refused, "No Source staging configured."));
    }

    public Func<SourceSetContextRequest, CancellationToken, Task<CommandOutcome<SourceStageResponse>>>? StageSourceContextHandler { get; set; }
    public List<SourceSetContextRequest> StageSourceContextRequests { get; } = [];
    public Task<CommandOutcome<SourceStageResponse>> StageSourceContextAsync(
        SourceSetContextRequest request, CancellationToken cancellationToken)
    {
        StageSourceContextRequests.Add(request);
        return StageSourceContextHandler?.Invoke(request, cancellationToken) ?? Refused<SourceStageResponse>(new Refusal("source.unconfigured", FailureReason.Refused, "No Source staging configured."));
    }

}
