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
/// It shows no start time, and a finished job's wall time only as whole seconds or minutes. Once a Ready status is
/// shown it reads the stored Reports behind it, only to present the Findings the Proposal adds; that reads nothing
/// else and starts nothing.
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
    private readonly ProposalParsimonyFindings? _findings;
    private string? _findingsKey;
    private IReadOnlyList<ProposalParsimonyGroupFindings> _findingsRead = [];
    private CancellationTokenSource? _cancellation;
    private Task _watching = Task.CompletedTask;
    private string? _projectPath;
    private int _generation;

    /// <summary>Creates the model.</summary>
    /// <param name="read">Reads one Proposal's Parsimony status; it must not queue or start anything.</param>
    /// <param name="delay">Waits between two reads; tests supply their own so no clock decides a result.</param>
    /// <param name="readReport">Reads one stored Report; with <paramref name="readView"/> it lists the Findings.</param>
    /// <param name="readView">Reads one stored named view page, for the Active Findings and the evidence.</param>
    public ProposalParsimonyViewModel(
        Func<ReadProposalParsimonyStatusRequest, CancellationToken, Task<CommandOutcome<ProposalParsimonyStatus>>> read,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<ShowParsimonyReportRequest, CancellationToken, Task<CommandOutcome<ParsimonyReportResponse>>>? readReport = null,
        Func<ReadParsimonyViewRequest, CancellationToken, Task<CommandOutcome<ParsimonyNamedViewResponse>>>? readView = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        _read = read;
        _delay = delay ?? ((span, cancellation) => Task.Delay(span, cancellation));
        if (readReport is not null && readView is not null) _findings = new ProposalParsimonyFindings(readReport, readView);
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

    /// <summary>The short read of what the measures found, or empty when there is nothing to read yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReadText))]
    private string _readText = string.Empty;

    /// <summary>The Trial's parser work as a percent change against the Baseline.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParserWork))]
    private string _parserWork = string.Empty;

    /// <summary>How long Parsimony took, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWallTime))]
    private string _wallTime = string.Empty;

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

    /// <summary>Whether the short read has something to say.</summary>
    public bool HasReadText => ReadText.Length > 0;

    /// <summary>Whether the Proposal has a Trial whose parser work is shown.</summary>
    public bool HasParserWork => ParserWork.Length > 0;

    /// <summary>Whether the finished job recorded a wall time.</summary>
    public bool HasWallTime => WallTime.Length > 0;

    /// <summary>The short read's heading.</summary>
    public string ReadTitle => MotifAppStrings.ProposalParsimonyReadTitle;

    /// <summary>The parser work line's label.</summary>
    public string ParserWorkLabel => MotifAppStrings.ProposalParsimonyParserWorkLabel;

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
        if (!same) DiscardFindings();
        // The same Proposal read again keeps what it showed, so a refresh never blanks the section.
        if (!same || Status is null) ShowReading(proposalId);
        _projectPath = projectPath;
        ProposalId = proposalId;
        var cancellation = _cancellation = new CancellationTokenSource();
        _watching = WatchAsync(projectPath, proposalId, ++_generation, cancellation.Token);
        return _watching;
    }

    /// <summary>Sets the parser work line from the selected Proposal's Trial; null clears it.</summary>
    /// <param name="work">The Trial's parser work against the Baseline, or null when the Proposal has no Trial.</param>
    public void SetParserWork(TrialWork? work) => ParserWork = ProposalParsimonyText.ParserWork(work);

    /// <summary>Stops watching and hides the section, as when the Proposal is closed.</summary>
    public void Stop()
    {
        EndWatch();
        DiscardFindings();
        _projectPath = null;
        ProposalId = null;
        Status = null;
        IsVisible = false;
        IsLoading = false;
        ShownRefusal = null;
        StateLabel = Sentence = Note = ReadText = WallTime = string.Empty;
        ParserWork = string.Empty;
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
                    if (!IsUnfinished(outcome.Value!, ref unqueued))
                    {
                        await ShowFindingsAsync(projectPath, outcome.Value!, generation, token).ConfigureAwait(true);
                        return;
                    }
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
        StateLabel = Sentence = Note = ReadText = WallTime = string.Empty;
        Groups = [];
    }

    private void Show(ProposalParsimonyStatus status)
    {
        Status = status;
        ShownRefusal = null;
        StateLabel = ProposalParsimonyText.StateLabel(status.State);
        Sentence = ProposalParsimonyText.Sentence(status);
        Note = ProposalParsimonyText.Note(status);
        ReadText = ReadOf(status);
        WallTime = ProposalParsimonyText.WallTime(status);
        Groups = [.. status.Groups.Select(group => ProposalParsimonyText.Group(group, status.State))];
        // The same candidate read again keeps its Findings, so a refresh never blanks them.
        if (_findingsKey is not null && _findingsKey == FindingsKey(status)) ApplyFindings(status);
        IsVisible = true;
        IsLoading = false;
    }

    // A group is read only when it is Ready and measured; the key names the stored Reports it would read.
    private static string? FindingsKey(ProposalParsimonyStatus status) => status.State != ProposalParsimonyState.Ready
        ? null
        : status.ProposalId + "|" + string.Join("|", status.Groups.Select(group =>
            group.State == ProposalParsimonyGroupState.Ready && group.Candidate is { } candidate
                ? candidate.CandidateIdentity + ":" + string.Join(",", group.Changes.Select(change => change.AfterReportId))
                : "-"));

    private async Task ShowFindingsAsync(string projectPath, ProposalParsimonyStatus status, int generation,
        CancellationToken token)
    {
        var key = FindingsKey(status);
        if (_findings is null || key is null) return;
        if (_findingsKey == key) return;
        var read = new List<ProposalParsimonyGroupFindings>();
        foreach (var group in status.Groups)
            read.Add(group.State == ProposalParsimonyGroupState.Ready && group.Candidate is not null
                ? await _findings.ReadAsync(projectPath, group, token).ConfigureAwait(true)
                : new ProposalParsimonyGroupFindings([], string.Empty, []));
        if (generation != _generation) return;
        CancelEvidence();
        _findingsRead = read;
        _findingsKey = key;
        ReadText = ReadOf(status);
        ApplyFindings(status);
    }

    private void ApplyFindings(ProposalParsimonyStatus status) =>
        Groups = [.. Groups.Select((group, index) => status.Groups[index].State == ProposalParsimonyGroupState.Ready &&
            status.Groups[index].Candidate is not null && index < _findingsRead.Count
            ? group with
            {
                Findings = _findingsRead[index].Findings, FindingsNote = _findingsRead[index].Note,
                Judgments = _findingsRead[index].Judgments, FindingsMore = _findingsRead[index].More,
                FindingsMoreLines = _findingsRead[index].MoreLines, HasFindingsRead = true,
            }
            : group)];

    // Cancels every evidence read under way and forgets the Findings, as when another Proposal is shown.
    private void DiscardFindings()
    {
        CancelEvidence();
        _findingsKey = null;
        _findingsRead = [];
    }

    private void CancelEvidence()
    {
        foreach (var finding in _findingsRead.SelectMany(group => group.Findings)) finding.Cancel();
    }

    // With a reader, the headline waits for the Findings so it never says what the list then contradicts.
    private string ReadOf(ProposalParsimonyStatus status)
    {
        if (_findings is null) return ProposalParsimonyText.Read(status);
        return _findingsKey is not null && _findingsKey == FindingsKey(status)
            ? ProposalParsimonyText.Read(status, ReadingsOf(status)) : ProposalParsimonyText.Caveats(status);
    }

    private ProposalParsimonyMeasureReading[] ReadingsOf(ProposalParsimonyStatus status) =>
        [.. _findingsRead.Take(status.Groups.Count).SelectMany(group => group.Readings)];

    private void ShowRefusal(Refusal refusal)
    {
        Status = null;
        ShownRefusal = WindowRefusal.From(refusal, MotifAppStrings.ProposalParsimonyUnreadable);
        StateLabel = Note = string.Empty;
        Sentence = MotifAppStrings.ProposalParsimonyUnreadable;
        ReadText = WallTime = string.Empty;
        Groups = [];
        IsVisible = true;
        IsLoading = false;
    }
}
