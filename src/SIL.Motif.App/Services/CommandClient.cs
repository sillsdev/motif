using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands;
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
/// <para>
/// The cancellation token goes to the command, never to <see cref="Task.Run(Action)"/>: a token that is already
/// cancelled when the work is queued would otherwise surface as a <see cref="TaskCanceledException"/> in the
/// view model instead of the command's own typed cancellation refusal, pinned by
/// <c>CommandClientCancellationTests.ACancellationBeforeTheCommandStartsIsStillATypedRefusal</c>.
/// </para>
/// <see cref="HandoffAsync"/> does not yet report through its <c>progress</c> parameter: there is no
/// in-process collaborator to produce Handoff progress steps from. The parameter exists so a caller can
/// already depend on the same <see cref="IProgress{T}"/> shape <see cref="AssessAsync"/> uses.
/// <para>
/// Every call that opens a project copy through LibLCM runs one at a time. LibLCM locks a project file
/// exclusively, and the window starts several such reads at once when a project is chosen — the grammar
/// check and the Text list — so without this the second fails with a locked-file error. A word trace joins them
/// because the parser reads that same file. The store-only reads do not, the Text words among them: those are
/// stored with the Baseline, so a superseded read neither waits behind LibLCM work nor holds it up.
/// </para>
/// </remarks>
public sealed partial class CommandClient : ICommandClient
{
    private readonly CommandClientOptions _options;
    private readonly string _managedRoot;
    private readonly SemaphoreSlim _projectGate = new(1, 1);

    public CommandClient() : this(CommandClientOptions.ForInstallation()) { }

    public CommandClient(CommandClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ManagedRoot);
        ArgumentNullException.ThrowIfNull(options.RunnerLauncher);
        var launched = options.RunnerLauncher.Options;
        if (!string.Equals(Path.GetFullPath(launched.Root), Path.GetFullPath(options.ManagedRoot),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(launched.ParserPath, options.ParserPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "The runner launcher must use the same worker root and parser as the command client.",
                nameof(options));
        _options = options;
        _managedRoot = options.ManagedRoot;
    }

    public Task<CommandOutcome<BaselineCaptureResponse>> CaptureBaselineAsync(
        BaselineCaptureRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => BaselineCaptureCommand.Capture(request, _managedRoot));

    public Task<IReadOnlyList<KnownProjectSummary>> ListKnownProjectsAsync(CancellationToken cancellationToken) =>
        Task.Run(() => KnownProjectsQuery.List(_managedRoot));

    public Task<CommandOutcome<CurrentBaselineResponse>> GetCurrentBaselineAsync(
        CurrentBaselineRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => CurrentBaselineQuery.Query(request));

    public Task<CommandOutcome<TextInventoryResponse>> ListTextsAsync(
        TextInventoryRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => TextInventoryQuery.Query(request));

    public Task<CommandOutcome<AssessCommandResponse>> AssessAsync(
        AssessRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return AfterStartGate(GatedCommand.Assess, () => OneAtATime(
            () => AssessCommand.Assess(request, _managedRoot, _options.ParserPath,
                progress.Report, cancellationToken)));
    }

    public Task<CommandOutcome<StatsCommandResponse>> StatsAsync(
        StatsRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => StatsCommand.Stats(request, _options.ParserPath, cancellationToken));

    public Task<CommandOutcome<HandoffCommandResponse>> HandoffAsync(
        HandoffRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return AfterStartGate(GatedCommand.Handoff, () => OneAtATime(
            () => HandoffCommand.Handoff(request, _managedRoot, _options.ParserPath,
                progress.Report, cancellationToken)));
    }

    private async Task<T> AfterStartGate<T>(GatedCommand command, Func<Task<T>> run)
    {
        if (_options.StartGate is { } gate) await gate.WaitToStartAsync(command).ConfigureAwait(false);
        return await run().ConfigureAwait(false);
    }

    // Waits without the caller's token, so a cancelled wait still reaches the command's own typed refusal.
    private Task<T> OneAtATime<T>(Func<T> work) => Task.Run(async () =>
    {
        await _projectGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return work();
        }
        finally
        {
            _projectGate.Release();
        }
    });

    private Task<T> OneAtATime<T>(Func<T> work, CancellationToken cancellationToken, Func<T> cancelled) =>
        Task.Run(async () =>
        {
            try
            {
                await _projectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return cancelled();
            }

            try
            {
                return work();
            }
            finally
            {
                _projectGate.Release();
            }
        });
}
