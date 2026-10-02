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
/// <see cref="HandoffAsync"/> reports command progress through the supplied <c>progress</c> callback.
/// <para>
/// Every call that opens a project copy through LibLCM runs one at a time. LibLCM locks a project file
/// exclusively, and the window starts several such reads at once when a project is chosen — the grammar
/// check and the Text list — so without this the second fails with a locked-file error. A word trace joins them
/// because the parser reads that same file. The store-only reads do not, the Text words among them: those are
/// stored with the Baseline, so a superseded read neither waits behind LibLCM work nor holds it up.
/// </para>
/// <para>
/// A call that has to wait for another stops waiting as soon as its token is cancelled and returns a
/// <c>project.wait-cancelled</c> refusal without starting its work, pinned by
/// <c>AdapterCancellationTests.ACallWaitingForTheProjectIsCancelledPromptlyAndNeverStarts</c>. A call that finds
/// the project free starts at once, so a command handed an already-cancelled token still answers with its own
/// cancellation refusal, or, when it takes no token, simply completes.
/// </para>
/// </remarks>
public sealed partial class CommandClient : ICommandClient
{
    private readonly CommandClientOptions _options;
    private readonly string _managedRoot;
    private readonly IProjectGate _projectGate;
    private readonly UsageRecorder _usageRecorder;
    private int _parseInFlight;

    public CommandClient() : this(CommandClientOptions.ForInstallation()) { }

    public CommandClient(CommandClientOptions options) : this(options, new ProjectGate()) { }

    internal CommandClient(CommandClientOptions options, IProjectGate projectGate)
    {
        ArgumentNullException.ThrowIfNull(projectGate);
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
        _projectGate = projectGate;
        _usageRecorder = options.UsageRecorder ?? UsageRecorder.ForMachineRoot(options.ManagedRoot);
    }

    public IDisposable BeginUsageAction(string command, params string[] argumentShape) =>
        _usageRecorder.BeginAction(command, argumentShape);

    public Task<CommandOutcome<BaselineCaptureResponse>> CaptureBaselineAsync(
        BaselineCaptureRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => BaselineCaptureCommand.Capture(
            request, _managedRoot, _options.TimeProvider ?? TimeProvider.System), cancellationToken);

    public Task<IReadOnlyList<KnownProjectSummary>> ListKnownProjectsAsync(CancellationToken cancellationToken) =>
        Task.Run(() => KnownProjectsQuery.List(_managedRoot));

    public Task<CommandOutcome<CurrentBaselineResponse>> GetCurrentBaselineAsync(
        CurrentBaselineRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => CurrentBaselineQuery.Query(request));

    public Task<CommandOutcome<TextInventoryResponse>> ListTextsAsync(
        TextInventoryRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => TextInventoryQuery.Query(request), cancellationToken);

    public Task<CommandOutcome<AssessCommandResponse>> AssessAsync(
        AssessRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return ParseOneAtATime(() => AfterStartGate(GatedCommand.Assess, cancellationToken, () => OneAtATime(
            () => AssessCommand.Assess(request, _managedRoot, _options.ParserPath,
                progress.Report, cancellationToken), cancellationToken)));
    }

    public Task<CommandOutcome<StatsCommandResponse>> StatsAsync(
        StatsRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => StatsCommand.Stats(request, _options.ParserPath, cancellationToken));

    public Task<CommandOutcome<HandoffCommandResponse>> HandoffAsync(
        HandoffRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return AfterStartGate(GatedCommand.Handoff, cancellationToken, () => OneAtATime(
            () => HandoffCommand.Handoff(request, _managedRoot, _options.ParserPath,
                progress.Report, cancellationToken), cancellationToken));
    }

    public Task<CommandOutcome<ProjectStoreResetResponse>> DeleteRefusedStoreAsync(
        ProjectStoreResetRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => ProjectStoreReset.DeleteRefused(request));

    private async Task<CommandOutcome<T>> ParseOneAtATime<T>(Func<Task<CommandOutcome<T>>> run) where T : class
    {
        if (Interlocked.CompareExchange(ref _parseInFlight, 1, 0) != 0)
            return CommandOutcome<T>.Refused(new Refusal("parse.already-running-here", FailureReason.Busy,
                "A parse is already running in this Motif window. Its progress is shown above."));
        try { return await run().ConfigureAwait(false); }
        finally { Volatile.Write(ref _parseInFlight, 0); }
    }

    private async Task<T> AfterStartGate<T>(
        GatedCommand command, CancellationToken cancellationToken, Func<Task<T>> run)
    {
        if (_options.StartGate is { } gate)
        {
            try
            {
                await gate.WaitToStartAsync(command).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Let the command produce its typed refusal without waiting for the held gate.
            }
        }

        return await run().ConfigureAwait(false);
    }

    // A free project starts the work at once, so a command handed a cancelled token reports that itself.
    private Task<CommandOutcome<T>> OneAtATime<T>(Func<CommandOutcome<T>> work, CancellationToken cancellationToken)
        where T : class =>
        Task.Run(async () =>
        {
            if (!_projectGate.TryEnter())
            {
                try
                {
                    await _projectGate.EnterAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return WaitCancelled<T>();
                }

                // The project can be handed over just as the caller cancels; a cancelled waiter still never starts.
                if (cancellationToken.IsCancellationRequested)
                {
                    _projectGate.Exit();
                    return WaitCancelled<T>();
                }
            }

            try
            {
                return work();
            }
            finally
            {
                _projectGate.Exit();
            }
        });

    private static CommandOutcome<T> WaitCancelled<T>() where T : class =>
        CommandOutcome<T>.Refused(new Refusal(
            "project.wait-cancelled", FailureReason.Cancelled, "Waiting to use the project was cancelled."));
}
