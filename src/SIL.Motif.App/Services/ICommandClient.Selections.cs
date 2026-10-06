using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    Task<CommandOutcome<DefaultSelectionResponse>> ReadDefaultSelectionAsync(
        ReadDefaultSelectionRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<DefaultSelectionResponse>> SetDefaultSelectionAsync(
        SetDefaultSelectionRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<NamedSelectionProjection>> SetSelectionLimitsAsync(
        SetSelectionLimitsRequest request, CancellationToken cancellationToken);

    /// <summary>Records that first-time setup was skipped for a project.</summary>
    Task<CommandOutcome<ProjectSetupResponse>> SkipSetupAsync(
        SkipSetupRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<ProjectConfigurationProjection>> ShowConfigAsync(
        ShowConfigRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<ParserStepRate>> ReadParserStepRateAsync(
        string projectPath, CancellationToken cancellationToken);
}
