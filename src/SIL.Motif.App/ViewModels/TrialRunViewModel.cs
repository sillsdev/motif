using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using C = SIL.Motif.Contract.Commands.RefusalCodes;

namespace SIL.Motif.App.ViewModels;

/// <summary>Where the window's own Try again of one Proposal stands.</summary>
/// <param name="ProposalId">The Proposal being tried.</param>
/// <param name="IsRunning">Whether the Trial is being queued or followed now.</param>
/// <param name="JobId">The queued Trial job, or <see langword="null"/> before one was queued.</param>
/// <param name="Progress">The latest word progress the Trial reported, if any.</param>
/// <param name="Refusal">Why the last Try again did not produce a result, if it did not.</param>
/// <param name="BaselineWarning">
/// Why the queued Trial was not compared against a Baseline, or <see langword="null"/> when it was.
/// </param>
public sealed record TrialRunState(string ProposalId, bool IsRunning, string? JobId, TrialWordProgress? Progress,
    Refusal? Refusal, string? BaselineWarning = null)
{
    /// <summary>
    /// The warning in the window's words, to show next to the Trial's result, or <see langword="null"/> when the Trial
    /// was compared against a Baseline.
    /// </summary>
    public string? BaselineNotice => TrialBaselineNotice.For(BaselineWarning);

    /// <summary>The refusal in the window's words, with the command's own account under Details.</summary>
    public WindowRefusal? ShownRefusal => Refusal is null ? null : WindowRefusal.From(Refusal);

    /// <summary>A Proposal the window has not tried since the project opened.</summary>
    public static TrialRunState Idle(string proposalId) => new(proposalId, false, null, null, null);
}

/// <summary>
/// The window's one Try again: it queues a Proposal's Trial with the word scope the Proposal's content calls for,
/// follows the job to its end with progress, and reads the Trial it produced. The bar, the Proposals cards, the
/// Review blocker and Try a Word all start it through <see cref="WorkspaceContext.TryAgainAsync"/>, and bind to
/// <see cref="States"/> for "Trying…".
/// </summary>
/// <remarks>
/// A second Try again of a Proposal already being tried joins the running one instead of queueing another Trial.
/// Different Proposals are tried side by side. Cancelling, which leaving the project does, cancels the queued
/// Trial too, as the Review page's Measure does.
/// </remarks>
public sealed partial class TrialRunViewModel : ObservableObject
{
    private readonly ICommandClient _commands;
    private readonly SynchronizationContext? _ownerContext;
    private readonly object _gate = new();
    private readonly Dictionary<string, Run> _running = new(StringComparer.Ordinal);
    private CancellationTokenSource _session = new();
    private ImmutableDictionary<string, TrialRunState> _current =
        ImmutableDictionary.Create<string, TrialRunState>(StringComparer.Ordinal);

    /// <summary>Creates the run over the window's command seam.</summary>
    public TrialRunViewModel(ICommandClient commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        _ownerContext = SynchronizationContext.Current;
    }

    /// <summary>Each Proposal the window tried since the project opened, keyed by Proposal id.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private ImmutableDictionary<string, TrialRunState> _states =
        ImmutableDictionary.Create<string, TrialRunState>(StringComparer.Ordinal);

    /// <summary>Whether any Proposal is being tried now.</summary>
    public bool IsActive => States.Values.Any(state => state.IsRunning);

    /// <summary>Where the window's Try again of <paramref name="proposalId"/> stands.</summary>
    public TrialRunState StateFor(string proposalId) =>
        States.GetValueOrDefault(proposalId) ?? TrialRunState.Idle(proposalId);

    /// <summary>Whether <paramref name="proposalId"/> is being tried now.</summary>
    public bool IsRunning(string proposalId) => StateFor(proposalId).IsRunning;

    /// <summary>Tries one Proposal, or joins the Try again already running for it.</summary>
    /// <returns>The Trial it produced, or a refusal saying why there is none; never an exception.</returns>
    internal Task<CommandOutcome<TrialRecord?>> RunAsync(string projectPath, string proposalId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        Run run;
        lock (_gate)
        {
            if (_running.TryGetValue(proposalId, out var running)) return running.Result.Task;
            run = new Run(proposalId, _session,
                CancellationTokenSource.CreateLinkedTokenSource(_session.Token, cancellationToken));
            _running[proposalId] = run;
            Set(proposalId, new TrialRunState(proposalId, true, null, null, null, null));
        }
        _ = RunCoreAsync(run, projectPath);
        return run.Result.Task;
    }

    /// <summary>
    /// Stops following every Trial, cancels them and forgets what the window tried, as when it leaves
    /// the project.
    /// </summary>
    public void CancelAll()
    {
        CancellationTokenSource stopped;
        lock (_gate)
        {
            stopped = _session;
            _session = new CancellationTokenSource();
            _running.Clear();
            _current = _current.Clear();
            Publish();
        }
        stopped.Cancel();
    }

    private async Task RunCoreAsync(Run run, string projectPath)
    {
        CommandOutcome<TrialRecord?> outcome;
        try
        {
            outcome = await TryAsync(run, projectPath).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            outcome = CommandOutcome<TrialRecord?>.Refused(new Refusal(C.TrialMeasurementIncomplete,
                FailureReason.Refused, exception.Message));
        }
        lock (_gate)
        {
            if (ReferenceEquals(run.Session, _session))
            {
                _running.Remove(run.ProposalId);
                Set(run.ProposalId, Current(run.ProposalId) with { IsRunning = false, Refusal = outcome.Refusal });
            }
        }
        run.Cancellation.Dispose();
        run.Result.SetResult(outcome);
    }

    private async Task<CommandOutcome<TrialRecord?>> TryAsync(Run run, string projectPath)
    {
        var token = run.Cancellation.Token;
        var queued = await _commands.EnqueueTrialAsync(new EnqueueTrialRequest(projectPath,
            MotifProductVersion.CurrentText, run.ProposalId), token).ConfigureAwait(true);
        if (!queued.Succeeded) return CommandOutcome<TrialRecord?>.Refused(queued.Refusal!);
        var jobId = queued.Value!.Job.JobId;
        var warning = queued.Value.BaselineWarning;
        lock (_gate)
        {
            if (ReferenceEquals(run.Session, _session))
                Set(run.ProposalId, Current(run.ProposalId) with { JobId = jobId, BaselineWarning = warning });
        }

        var waited = await _commands.WaitForJobAsync(new WaitForJobRequest(projectPath, jobId,
            MotifProductVersion.CurrentText, Timeout.InfiniteTimeSpan), new StatusProgress(this, run), token)
            .ConfigureAwait(true);
        if (!waited.Succeeded) return CommandOutcome<TrialRecord?>.Refused(waited.Refusal!);
        if (waited.Value!.Status != JobStatus.Completed)
        {
            return CommandOutcome<TrialRecord?>.Refused(new Refusal(C.TrialMeasurementIncomplete,
                FailureReason.Refused, "Trial job '" + jobId + "' finished as " +
                JobStatusJson.ToWire(waited.Value.Status ?? JobStatus.Failed) + " rather than completing."));
        }
        return await _commands.ReadTrialAsync(new ReadTrialRequest(projectPath, run.ProposalId), token)
            .ConfigureAwait(true);
    }

    private void Report(Run run, JobStatusResponse status)
    {
        if (status.TrialProgress is not { } progress) return;
        lock (_gate)
        {
            if (!_running.TryGetValue(run.ProposalId, out var current) || !ReferenceEquals(current, run)) return;
            Set(run.ProposalId, Current(run.ProposalId) with { Progress = progress });
        }
    }

    private TrialRunState Current(string proposalId) =>
        _current.GetValueOrDefault(proposalId) ?? TrialRunState.Idle(proposalId);

    // Callers hold the gate; the window's thread is told, in order, through its own context when it has one.
    private void Set(string proposalId, TrialRunState state)
    {
        _current = _current.SetItem(proposalId, state);
        Publish();
    }

    private void Publish()
    {
        if (_ownerContext is null || SynchronizationContext.Current == _ownerContext) States = _current;
        else _ownerContext.Post(static self => ((TrialRunViewModel)self!).PublishStates(), this);
    }

    private void PublishStates()
    {
        lock (_gate) States = _current;
    }

    private sealed class Run(string proposalId, CancellationTokenSource session, CancellationTokenSource cancellation)
    {
        public string ProposalId { get; } = proposalId;
        public CancellationTokenSource Session { get; } = session;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public TaskCompletionSource<CommandOutcome<TrialRecord?>> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class StatusProgress(TrialRunViewModel owner, Run run) : IProgress<JobStatusResponse>
    {
        public void Report(JobStatusResponse value) => owner.Report(run, value);
    }
}
