using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public Task<CommandOutcome<SourceListResponse>> ListSourcesAsync(
        SourceListRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => SourceQuery.List(request), cancellationToken);

    public Task<CommandOutcome<SourceShowResponse>> ShowSourceAsync(
        SourceShowRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => SourceQuery.Show(request), cancellationToken);

    public Task<CommandOutcome<SourceWhereResponse>> WhereSourceAsync(
        SourceWhereRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => SourceQuery.Where(request), cancellationToken);

    public Task<CommandOutcome<CurrentGetResponse>> GetSourceCurrentAsync(
        CurrentGetRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => SourceQuery.CurrentGet(request), cancellationToken);

    public Task<CommandOutcome<SourceLocalResponse>> SetSourceFolderAsync(
        SourceSetFolderRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => SourceLocalCommand.SetFolder(request), cancellationToken);

    public Task<CommandOutcome<SourceLocalResponse>> ClearSourceFolderAsync(
        SourceClearFolderRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => SourceLocalCommand.ClearFolder(request), cancellationToken);

    public Task<CommandOutcome<SourceLocalResponse>> SetSourceCurrentAsync(
        CurrentSetRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => SourceLocalCommand.SetCurrent(request), cancellationToken);

    public Task<CommandOutcome<SourceLocalResponse>> ClearSourceCurrentAsync(
        CurrentClearRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => SourceLocalCommand.ClearCurrent(request), cancellationToken);

    public Task<CommandOutcome<SourceStageResponse>> StageSourceAddAsync(
        SourceAddRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => ProposalCommands.StageSourceAdd(request), cancellationToken);

    public Task<CommandOutcome<SourceStageResponse>> StageSourceContextAsync(
        SourceSetContextRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => ProposalCommands.StageSourceSetContext(request), cancellationToken);

}
