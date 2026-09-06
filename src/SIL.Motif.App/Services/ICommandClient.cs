using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

/// <summary>
/// The one seam every view model calls through to reach a command or a read-only query. Each method
/// mirrors one catalogued command's own request and <see cref="CommandOutcome{T}"/> shape exactly, or —
/// for <see cref="ListKnownProjectsAsync"/>, <see cref="GetCurrentBaselineAsync"/>, and
/// <see cref="ListTextsAsync"/>, which are queries with no CLI verb — the equivalent read. A view model
/// never shells out to the CLI and never parses JSON; <see cref="CommandClient"/> runs everything
/// in-process, and a deterministic fake stands in for it in tests.
/// </summary>
public interface ICommandClient
{
    Task<CommandOutcome<BaselineCaptureResponse>> CaptureBaselineAsync(
        BaselineCaptureRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<KnownProjectSummary>> ListKnownProjectsAsync(CancellationToken cancellationToken);

    Task<CommandOutcome<CurrentBaselineResponse>> GetCurrentBaselineAsync(
        CurrentBaselineRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<TextInventoryResponse>> ListTextsAsync(
        TextInventoryRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<AssessCommandResponse>> AssessAsync(
        AssessRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken);

    Task<CommandOutcome<StatsCommandResponse>> StatsAsync(
        StatsRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<HandoffCommandResponse>> HandoffAsync(
        HandoffRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken);
}
