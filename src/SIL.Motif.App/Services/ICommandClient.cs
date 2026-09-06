using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

/// <summary>
/// The one seam every view model calls through to reach a command. Each method mirrors one catalogued
/// command's own request and <see cref="CommandOutcome{T}"/> shape exactly, so a view model never shells
/// out to the CLI and never parses JSON — <see cref="CommandClient"/> runs the command in-process, and a
/// deterministic fake stands in for it in tests.
/// </summary>
public interface ICommandClient
{
    Task<CommandOutcome<BaselineCaptureResponse>> CaptureBaselineAsync(
        BaselineCaptureRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<AssessCommandResponse>> AssessAsync(
        AssessRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken);

    Task<CommandOutcome<StatsCommandResponse>> StatsAsync(
        StatsRequest request, CancellationToken cancellationToken);

    Task<CommandOutcome<HandoffCommandResponse>> HandoffAsync(
        HandoffRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken);
}
