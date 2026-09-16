using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

/// <summary>
/// Runs the four catalog commands in-process, off the calling thread, so awaiting on the UI thread never
/// blocks it. Delegates to the same static entry points the CLI calls; nothing here shells out or reads a
/// process's JSON.
/// </summary>
/// <remarks>
/// <see cref="HandoffAsync"/> does not yet report through its <c>progress</c> parameter: there is no
/// in-process collaborator to produce Handoff progress steps from. The parameter exists so a caller can
/// already depend on the same <see cref="IProgress{T}"/> shape <see cref="AssessAsync"/> uses.
/// </remarks>
public sealed class CommandClient : ICommandClient
{
    private readonly string _managedRoot;

    public CommandClient() : this(SIL.Motif.Worker.RunnerOptions.ResolveRoot()) { }

    public CommandClient(string managedRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        _managedRoot = managedRoot;
    }

    public Task<CommandOutcome<BaselineCaptureResponse>> CaptureBaselineAsync(
        BaselineCaptureRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => BaselineCaptureCommand.Capture(request, _managedRoot), cancellationToken);

    public Task<IReadOnlyList<KnownProjectSummary>> ListKnownProjectsAsync(CancellationToken cancellationToken) =>
        Task.Run(() => KnownProjectsQuery.List(_managedRoot), cancellationToken);

    public Task<CommandOutcome<CurrentBaselineResponse>> GetCurrentBaselineAsync(
        CurrentBaselineRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => CurrentBaselineQuery.Query(request), cancellationToken);

    public Task<CommandOutcome<TextInventoryResponse>> ListTextsAsync(
        TextInventoryRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => TextInventoryQuery.Query(request), cancellationToken);

    public Task<CommandOutcome<AssessCommandResponse>> AssessAsync(
        AssessRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return Task.Run(
            () => AssessCommand.Assess(request, _managedRoot, progress.Report, cancellationToken), cancellationToken);
    }

    public Task<CommandOutcome<StatsCommandResponse>> StatsAsync(
        StatsRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => StatsCommand.Stats(request, cancellationToken), cancellationToken);

    public Task<CommandOutcome<HandoffCommandResponse>> HandoffAsync(
        HandoffRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return Task.Run(
            () => HandoffCommand.Handoff(request, _managedRoot, progress.Report, cancellationToken), cancellationToken);
    }
}
