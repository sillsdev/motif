using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    Task<CommandOutcome<SourceListResponse>> ListSourcesAsync(
        SourceListRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<SourceShowResponse>> ShowSourceAsync(
        SourceShowRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<SourceWhereResponse>> WhereSourceAsync(
        SourceWhereRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<CurrentGetResponse>> GetSourceCurrentAsync(
        CurrentGetRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<SourceLocalResponse>> SetSourceFolderAsync(
        SourceSetFolderRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<SourceLocalResponse>> ClearSourceFolderAsync(
        SourceClearFolderRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<SourceLocalResponse>> SetSourceCurrentAsync(
        CurrentSetRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<SourceLocalResponse>> ClearSourceCurrentAsync(
        CurrentClearRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<SourceStageResponse>> StageSourceAddAsync(
        SourceAddRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<SourceStageResponse>> StageSourceContextAsync(
        SourceSetContextRequest request, CancellationToken cancellationToken);

}
