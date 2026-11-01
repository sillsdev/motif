using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The selected Proposal's automatic Parsimony status, kept up to date while its job is queued or running.
/// </summary>
/// <remarks>
/// Parsimony is advice that a committed Trial queues by itself, so this model only reads: its one command is the
/// status read, which queues nothing and starts no parse. It polls while the job is queued or running, every
/// <see cref="PollInterval"/> at first and every <see cref="SlowPollInterval"/> after <see cref="FastPolls"/> reads, and
/// stops when the state is final, the Proposal is closed or another is chosen. A busy project never ends the watch.
/// It shows no start time or duration, because the status carries none.
/// </remarks>
public sealed partial class ProposalParsimonyViewModel : ObservableObject
{
    /// <summary>How long the model waits between two reads of a job that is not finished.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>How long the model waits between two reads once <see cref="FastPolls"/> reads have not finished the job.</summary>
    public static readonly TimeSpan SlowPollInterval = TimeSpan.FromSeconds(30);

    /// <summary>How many reads wait <see cref="PollInterval"/>, about five minutes, before the watch slows down.</summary>
    public const int FastPolls = 150;

    /// <summary>
    /// How many reads watch for a job that a finished Trial has not queued yet; the queue follows the Trial at once,
    /// so a few reads cover the gap and a Proposal with no job after them is left as not yet run.
    /// </summary>
    public const int NotQueuedGraceReads = 3;

    private readonly Func<ReadProposalParsimonyStatusRequest, CancellationToken,
        Task<CommandOutcome<ProposalParsimonyStatus>>> _read;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private CancellationTokenSource? _cancellation;
    private Task _watching = Task.CompletedTask;
    private string? _projectPath;
    private int _generation;

    /// <summary>Creates the model.</summary>
    /// <param name="read">Reads one Proposal's Parsimony status; it must not queue or start anything.</param>
    /// <param name="delay">Waits between two reads; tests supply their own so no clock decides a result.</param>
    public ProposalParsimonyViewModel(
        Func<ReadProposalParsimonyStatusRequest, CancellationToken, Task<CommandOutcome<ProposalParsimonyStatus>>> read,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        _read = read;
        _delay = delay ?? ((span, cancellation) => Task.Delay(span, cancellation));
    }

    /// <summary>The Proposal the section is about, or null when none is watched.</summary>
    [ObservableProperty]
    private string? _proposalId;

    /// <summary>Whether the section is shown: a Proposal is watched and its first read has begun.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStatus))]
    private bool _isVisible;

    /// <summary>Whether the first read of this Proposal's status is under way.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStatus))]
    private bool _isLoading;

    /// <summary>The overall state in the window's words, such as Ready or Did Not Finish.</summary>
    [ObservableProperty]
    private string _stateLabel = string.Empty;

    /// <summary>One plain sentence about the state.</summary>
    [ObservableProperty]
    private string _sentence = string.Empty;

    /// <summary>A second sentence the state needs, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string _note = string.Empty;

    /// <summary>The Grammar group, then the Text group.</summary>
    [ObservableProperty]
    private IReadOnlyList<ProposalParsimonyGroupViewModel> _groups = [];

    /// <summary>Why the status could not be read, or null.</summary>
    [ObservableProperty]
    private WindowRefusal? _shownRefusal;

    /// <summary>The last status read, or null.</summary>
    public ProposalParsimonyStatus? Status { get; private set; }

    /// <summary>Whether a status, not a read in progress, is shown.</summary>
    public bool ShowsStatus => IsVisible && !IsLoading;

    /// <summary>Whether the state has a second sentence.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>The words shown while the first read is under way.</summary>
    public string ReadingText => MotifAppStrings.ProposalParsimonyReading;

    /// <summary>
    /// Shows <paramref name="proposalId"/>'s status and keeps it current while its job is not finished. Watching the
    /// same Proposal again while a watch is under way returns that watch; another Proposal replaces it.
    /// </summary>
    /// <param name="projectPath">The open project.</param>
    /// <param name="proposalId">The Proposal to show.</param>
    /// <returns>A task that completes when the watch ends.</returns>
    public Task Watch(string projectPath, string proposalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        var same = _projectPath == projectPath && ProposalId == proposalId;
        if (same && !_watching.IsCompleted) return _watching;
        EndWatch();
        // The same Proposal read again keeps what it showed, so a refresh never blanks the section.
        if (!same || Status is null) ShowReading(proposalId);
        _projectPath = projectPath;
        ProposalId = proposalId;
        var cancellation = _cancellation = new CancellationTokenSource();
        _watching = WatchAsync(projectPath, proposalId, ++_generation, cancellation.Token);
        return _watching;
    }

    /// <summary>Stops watching and hides the section, as when the Proposal is closed.</summary>
    public void Stop()
    {
        EndWatch();
        _projectPath = null;
        ProposalId = null;
        Status = null;
        IsVisible = false;
        IsLoading = false;
        ShownRefusal = null;
        StateLabel = Sentence = Note = string.Empty;
        Groups = [];
    }

    private void EndWatch()
    {
        ++_generation;
        var cancellation = _cancellation;
        _cancellation = null;
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private async Task WatchAsync(string projectPath, string proposalId, int generation, CancellationToken token)
    {
        try
        {
            var polls = 0;
            var unqueued = 0;
            while (true)
            {
                var request = new ReadProposalParsimonyStatusRequest(projectPath, MotifProductVersion.CurrentText,
                    proposalId);
                var outcome = await _read(request, token).ConfigureAwait(true);
                if (generation != _generation) return;
                if (!outcome.Succeeded)
                {
                    // A busy project says nothing about the job, so the last good state stays and the watch goes on.
                    if (outcome.Refusal!.Reason != FailureReason.Busy)
                    {
                        ShowRefusal(outcome.Refusal);
                        return;
                    }
                }
                else
                {
                    Show(outcome.Value!);
                    if (!IsUnfinished(outcome.Value!, ref unqueued)) return;
                }
                await _delay(polls++ < FastPolls ? PollInterval : SlowPollInterval, token).ConfigureAwait(true);
                if (generation != _generation) return;
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled wait ends the watch and leaves what was last shown.
        }
    }

    private static bool IsUnfinished(ProposalParsimonyStatus status, ref int unqueued) => status.State switch
    {
        ProposalParsimonyState.Queued or ProposalParsimonyState.Running => true,
        ProposalParsimonyState.NotYetRun => status.StateReason?.Code == ProposalParsimonyText.NotQueuedCode &&
            ++unqueued <= NotQueuedGraceReads,
        _ => false,
    };

    private void ShowReading(string proposalId)
    {
        Status = null;
        ProposalId = proposalId;
        IsVisible = true;
        IsLoading = true;
        ShownRefusal = null;
        StateLabel = Sentence = Note = string.Empty;
        Groups = [];
    }

    private void Show(ProposalParsimonyStatus status)
    {
        Status = status;
        ShownRefusal = null;
        StateLabel = ProposalParsimonyText.StateLabel(status.State);
        Sentence = ProposalParsimonyText.Sentence(status);
        Note = ProposalParsimonyText.Note(status);
        Groups = [.. status.Groups.Select(group => ProposalParsimonyText.Group(group, status.State))];
        IsVisible = true;
        IsLoading = false;
    }

    private void ShowRefusal(Refusal refusal)
    {
        Status = null;
        ShownRefusal = WindowRefusal.From(refusal, MotifAppStrings.ProposalParsimonyUnreadable);
        StateLabel = Note = string.Empty;
        Sentence = MotifAppStrings.ProposalParsimonyUnreadable;
        Groups = [];
        IsVisible = true;
        IsLoading = false;
    }
}
