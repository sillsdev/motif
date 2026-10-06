using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Host;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public Task<CommandOutcome<DefaultSelectionResponse>> ReadDefaultSelectionAsync(
        ReadDefaultSelectionRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => SelectionCommands.ReadDefault(request));

    public Task<CommandOutcome<DefaultSelectionResponse>> SetDefaultSelectionAsync(
        SetDefaultSelectionRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => SelectionCommands.SetDefault(request), cancellationToken);

    public Task<CommandOutcome<NamedSelectionProjection>> SetSelectionLimitsAsync(
        SetSelectionLimitsRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => SelectionCommands.SetLimits(request), cancellationToken);

    public Task<CommandOutcome<ProjectSetupResponse>> SkipSetupAsync(
        SkipSetupRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => ProjectSetupCommands.Skip(request), cancellationToken);

    public Task<CommandOutcome<ProjectConfigurationProjection>> ShowConfigAsync(
        ShowConfigRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => ConfigCommands.Show(request));

    public Task<CommandOutcome<ParserStepRate>> ReadParserStepRateAsync(
        string projectPath, CancellationToken cancellationToken) =>
        Task.Run(() => SelectionLimitEstimateQuery.ReadParserStepRate(projectPath), cancellationToken);
}
