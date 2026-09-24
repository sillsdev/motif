using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.App.ViewModels;

/// <summary>The kinds of change a person can collect about a word's analyses, before any is written.</summary>
public static class ChangeKinds
{
    /// <summary>Approve the analysis: the project's candidate, or the parser's reading.</summary>
    public const string Approve = "approve";

    /// <summary>Reject the analysis, so FieldWorks never offers it as a guess.</summary>
    public const string Reject = "reject";

    /// <summary>Take back a human opinion, leaving the analysis a candidate again.</summary>
    public const string Candidate = "candidate";

    /// <summary>Mark the word's spelling as incorrect in FieldWorks.</summary>
    public const string IncorrectSpelling = "incorrect-spelling";

    /// <summary>Send the parser's reading to FieldWorks as a new candidate analysis.</summary>
    public const string AddCandidate = "add-candidate";

    /// <summary>The words a change of <paramref name="kind"/> is listed with.</summary>
    public static string LabelOf(string kind) => kind switch
    {
        Approve => "Approve",
        Reject => "Reject",
        Candidate => "Back to candidate",
        IncorrectSpelling => "Incorrect spelling",
        AddCandidate => "Add as candidate",
        _ => kind,
    };

    /// <summary>
    /// Whether Motif's Proposal language can already express this change. Spelling status is an operation today;
    /// opinions on analyses and new analyses are classified for Proposals but not yet built.
    /// </summary>
    public static bool CanBeProposedToday(string kind) => kind == IncorrectSpelling;
}

/// <summary>
/// The observable view of the pending Draft and each change's current fit with the saved project.
/// </summary>
public sealed partial class ChangesViewModel : ObservableObject
{
    private readonly ICommandClient? _client;
    public ChangesViewModel(ICommandClient? client = null)
    {
        _client = client;
        RemoveCommand = new AsyncRelayCommand<ChangeViewModel>(RemoveAsync);
        ClearCommand = new AsyncRelayCommand(ClearAsync);
        Items.CollectionChanged += (_, _) => Raise();
    }

    public ObservableCollection<ChangeViewModel> Items { get; } = [];

    /// <summary>The project these changes belong to, or <see langword="null"/> before one is open.</summary>
    public string? ProjectPath { get; private set; }

    /// <summary>Loads the pending changes belonging to <paramref name="projectPath"/>.</summary>
    public async Task OpenProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ProjectPath = projectPath;
        Reset();
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>How many changes wait.</summary>
    public int Count => Items.Count;

    public IAsyncRelayCommand<ChangeViewModel> RemoveCommand { get; }
    public IAsyncRelayCommand ClearCommand { get; }

    public PendingChangesSnapshot Snapshot { get; private set; } = new(null, "none", [], []);

    public Refusal? LastRefusal { get; private set; }

    public string? ErrorText => LastRefusal?.Message;

    public string? AssessmentId { get; set; }

    public async Task SetProjectAsync(string path, CancellationToken cancellationToken = default)
        => await OpenProjectAsync(path, cancellationToken).ConfigureAwait(true);

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (_client is null || ProjectPath is null) return;
        Accept(await _client.LoadPendingChangesAsync(new PendingChangesRequest(
            ProjectPath, MotifProductVersion.CurrentText), cancellationToken).ConfigureAwait(true));
    }

    public async Task PutAsync(ChangeIntent change, CancellationToken cancellationToken = default)
    {
        if (_client is null || ProjectPath is null) return;
        var outcome = await _client.PutPendingChangeAsync(new PutPendingChangeRequest(
            ProjectPath, MotifProductVersion.CurrentText, Snapshot.Revision, change),
            cancellationToken).ConfigureAwait(true);
        Accept(outcome);
        if (outcome.Refusal?.Code == "change.revision-conflict")
            await ReloadAfterConflictAsync(outcome.Refusal, cancellationToken);
    }

    public async Task AddAsync(string kind, CompareWordViewModel word)
    {
        if (_client is null || ProjectPath is null)
        {
            Add(kind, word);
            return;
        }
        await PutAsync(new ChangeIntent(CanonicalId.Mint().Value, kind, "", word.Word,
            AssessmentId, word.Reading, DisplayReading: word.FirstReading)).ConfigureAwait(true);
    }

    private async Task RemoveAsync(ChangeViewModel? change)
    {
        if (change is null) return;
        if (_client is null || ProjectPath is null)
        {
            Items.Remove(change);
            Raise();
            return;
        }
        var outcome = await _client.RemovePendingChangeAsync(new RemovePendingChangeRequest(
            ProjectPath, MotifProductVersion.CurrentText, Snapshot.Revision, change.ChangeId),
            CancellationToken.None).ConfigureAwait(true);
        Accept(outcome);
        if (outcome.Refusal?.Code == "change.revision-conflict")
            await ReloadAfterConflictAsync(outcome.Refusal, CancellationToken.None);
    }

    private async Task ReloadAfterConflictAsync(Refusal conflict, CancellationToken cancellationToken)
    {
        await ReloadAsync(cancellationToken);
        if (LastRefusal is not null) return;
        LastRefusal = conflict;
        OnPropertyChanged(nameof(LastRefusal));
        OnPropertyChanged(nameof(ErrorText));
    }

    private async Task ClearAsync()
    {
        foreach (var change in Items.ToArray()) await RemoveAsync(change).ConfigureAwait(true);
    }

    public void Reset()
    {
        Items.Clear();
        Snapshot = new PendingChangesSnapshot(null, "none", [], []);
        LastRefusal = null;
        AssessmentId = null;
        OnPropertyChanged(nameof(Snapshot));
        Raise();
    }

    private void Accept(CommandOutcome<PendingChangesSnapshot> outcome)
    {
        LastRefusal = outcome.Refusal;
        OnPropertyChanged(nameof(LastRefusal));
        OnPropertyChanged(nameof(ErrorText));
        if (outcome.Value is not { } snapshot) return;
        Snapshot = snapshot;
        OnPropertyChanged(nameof(Snapshot));
        Items.Clear();
        foreach (var change in snapshot.Changes)
        {
            var fit = snapshot.FitSummary.FirstOrDefault(item => item.ChangeId == change.ChangeId);
            Items.Add(new ChangeViewModel(change.Kind, change.Word, "Project analysis",
                change.DisplayReading ?? "", change.ChangeId, fit));
        }
        Raise();
    }

    public bool HasItems => Items.Count > 0;

    /// <summary>How many changes wait, as the Texts page's strip says it.</summary>
    public string CountText => Items.Count == 1 ? "1 change not applied yet" : $"{Items.Count:N0} changes not applied yet";

    public string Summary => Items.Count switch
    {
        0 => "No changes collected yet. Tick words below, then choose what should happen to them.",
        1 => "1 change collected",
        var count => $"{count:N0} changes collected",
    };

    /// <summary>What applying now could write to FieldWorks, and what has to wait for Motif to learn it.</summary>
    public string ApplyStatus
    {
        get
        {
            var stale = Items.Count(item => item.Fit is { StillFits: false });
            if (stale > 0)
                return stale == 1 ? "1 change no longer fits the project. Review is blocked."
                    : $"{stale:N0} changes no longer fit the project. Review is blocked.";
            var ready = Items.Count(item => item.CanBeProposedToday);
            var waiting = Items.Count - ready;
            return waiting == 0
                ? "All of these can be applied to the FieldWorks project."
                : $"{ready:N0} can be applied today; {waiting:N0} wait until Motif can apply approvals and new analyses.";
        }
    }

    /// <summary>Collects a change for <paramref name="word"/>, replacing any change already collected for it.</summary>
    public void Add(string kind, CompareWordViewModel word)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (Items.FirstOrDefault(item => item.Word == word.Word) is { } existing) Items.Remove(existing);
        Items.Add(new ChangeViewModel(kind, word.Word, word.RowLabel, word.FirstReading));
        Raise();
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(ApplyStatus));
    }
}

/// <summary>One collected change: what should happen to one word, and what it held when the change was chosen.</summary>
public sealed class ChangeViewModel(string kind, string word, string heldLabel, string reading,
    string? changeId = null, ChangeFit? fit = null)
{
    public string ChangeId { get; } = changeId ?? CanonicalId.Mint().Value;
    public ChangeFit? Fit { get; } = fit;
    public string FitStatus => Fit is null ? string.Empty : Fit.StillFits
        ? "Still fits the project." : "No longer fits: " + string.Join(" ", Fit.Reasons);
    public string Kind { get; } = kind;
    public string Label { get; } = ChangeKinds.LabelOf(kind);
    public string Word { get; } = word;
    public string HeldLabel { get; } = heldLabel;

    /// <summary>The parser's reading, for a change that sends it to FieldWorks or judges it.</summary>
    public string Reading { get; } = reading;

    public bool CanBeProposedToday { get; } = ChangeKinds.CanBeProposedToday(kind);

    public string Summary => $"{Word}: {Label}";
}
