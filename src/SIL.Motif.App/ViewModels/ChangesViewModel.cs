using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
/// The changes collected so far about words' analyses, meant to become one Proposal and be applied to FieldWorks
/// together. A word gets at most one change: choosing another replaces the first.
/// </summary>
public sealed partial class ChangesViewModel : ObservableObject
{
    public ChangesViewModel()
    {
        RemoveCommand = new RelayCommand<ChangeViewModel>(change =>
        {
            if (change is not null) Items.Remove(change);
            Raise();
        });
        ClearCommand = new RelayCommand(() =>
        {
            Items.Clear();
            Raise();
        });
    }

    public ObservableCollection<ChangeViewModel> Items { get; } = [];

    public IRelayCommand<ChangeViewModel> RemoveCommand { get; }
    public IRelayCommand ClearCommand { get; }

    public bool HasItems => Items.Count > 0;

    public string Summary => Items.Count switch
    {
        0 => "No changes collected yet. Tick words below, then choose what should happen to them.",
        1 => "1 change collected",
        var count => $"{count:N0} changes collected",
    };

    /// <summary>What a Proposal made now could hold, and what has to wait for Motif to learn it.</summary>
    public string ProposalStatus
    {
        get
        {
            var ready = Items.Count(item => item.CanBeProposedToday);
            var waiting = Items.Count - ready;
            return waiting == 0
                ? "All of these can become a Proposal."
                : $"{ready:N0} can become a Proposal today; {waiting:N0} wait for Motif's approval and new-analysis operations.";
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
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ProposalStatus));
    }
}

/// <summary>One collected change: what should happen to one word, and what it held when the change was chosen.</summary>
public sealed class ChangeViewModel(string kind, string word, string heldLabel, string reading)
{
    public string Kind { get; } = kind;
    public string Label { get; } = ChangeKinds.LabelOf(kind);
    public string Word { get; } = word;
    public string HeldLabel { get; } = heldLabel;

    /// <summary>The parser's reading, for a change that sends it to FieldWorks or judges it.</summary>
    public string Reading { get; } = reading;

    public bool CanBeProposedToday { get; } = ChangeKinds.CanBeProposedToday(kind);

    public string Summary => $"{Word}: {Label}";
}
