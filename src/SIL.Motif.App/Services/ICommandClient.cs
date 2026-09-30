using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

/// <summary>
/// The one seam every view model calls through to reach a catalogued command or a read-only query. Each
/// method mirrors its typed request and <see cref="CommandOutcome{T}"/> shape. A view model never shells out
/// to the CLI and never parses JSON; <see cref="CommandClient"/> runs everything in-process, and a deterministic
/// fake stands in for it in tests.
/// </summary>
public partial interface ICommandClient
{
    Task<CommandOutcome<BaselineCaptureResponse>> CaptureBaselineAsync(
        BaselineCaptureRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<KnownProjectSummary>> ListKnownProjectsAsync(CancellationToken cancellationToken);

    Task<CommandOutcome<CurrentBaselineResponse>> GetCurrentBaselineAsync(
        CurrentBaselineRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<TextInventoryResponse>> ListTextsAsync(
        TextInventoryRequest request, CancellationToken cancellationToken);

    /// <summary>Reads or changes Read state saved in the project's Motif store.</summary>
    Task<CommandOutcome<WordReadStateResponse>> ReadWordStateAsync(
        WordReadStateRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<AssessCommandResponse>> AssessAsync(
        AssessRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken);

    Task<CommandOutcome<StatsCommandResponse>> StatsAsync(
        StatsRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<HandoffCommandResponse>> HandoffAsync(
        HandoffRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the project's Motif store when another version of Motif made it, so opening the project again
    /// recreates it. Every change not yet applied is lost with it; the FieldWorks project is never touched.
    /// </summary>
    Task<CommandOutcome<ProjectStoreResetResponse>> DeleteRefusedStoreAsync(
        ProjectStoreResetRequest request, CancellationToken cancellationToken);
}
