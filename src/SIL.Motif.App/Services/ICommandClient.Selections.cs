using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    Task<CommandOutcome<DefaultSelectionResponse>> ReadDefaultSelectionAsync(
        ReadDefaultSelectionRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<DefaultSelectionResponse>> SetDefaultSelectionAsync(
        SetDefaultSelectionRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<ProjectConfigurationProjection>> ShowConfigAsync(
        ShowConfigRequest request, CancellationToken cancellationToken);
}
