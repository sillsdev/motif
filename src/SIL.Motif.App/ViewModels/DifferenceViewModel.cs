using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>What a word's move between two cells of the Compare matrix means, which orders and colours it.</summary>
public enum MoveKind
{
    /// <summary>Into a violation, or out of coverage: a person's decision or a working analysis was lost.</summary>
    Regressed,

    /// <summary>A timeout or an untried word now: nothing can be said about it any more.</summary>
    NowUnknown,

    /// <summary>Between two cells of the same standing, such as confirming a candidate and differing from it.</summary>
    Changed,

    /// <summary>A word nobody could analyse now has an analysis from the parser.</summary>
    NewCoverage,

    /// <summary>Out of a violation, into a cell that keeps what a person decided.</summary>
    Fixed,

    /// <summary>
    /// Up or down a row into a person's decision: approved, rejected or marked misspelled since the earlier run. This
    /// is the move a change to analyses makes, where a change to the grammar moves a word between columns.
    /// </summary>
    Decided,

    /// <summary>A word that was unknown now has an answer that breaks nothing.</summary>
    Settled,

    /// <summary>Into a better cell that none of the kinds above names.</summary>
    Improved,

    /// <summary>The same cell in both runs.</summary>
    Unchanged,
}

/// <summary>
/// "What changed" between two Assessments of the same words, as the Results stage shows it: each side placed in
/// the Compare matrix, and every word that moved between cells grouped by where it went, regressions first. Only
/// words both Assessments measured are compared; the rest are counted, never guessed at.
/// </summary>
/// <remarks>
/// This is a view over two results the app already holds. Motif can also store a Difference as an Assessment of its
/// own, which is what makes one citable; this view does not write one.
/// </remarks>
public sealed partial class DifferenceViewModel : ObservableObject
{
    public DifferenceViewModel()
    {
        ClearCommand = new RelayCommand(() => Load(null, null, string.Empty, string.Empty));
        OpenWordCommand = new RelayCommand<MovedWordViewModel>(word =>
        {
            if (word is not null) OpenWord?.Invoke(word.Word);
        });
    }

    /// <summary>Opens a word in the Words view; set by the workspace.</summary>
    public Action<string>? OpenWord { get; set; }

    public IRelayCommand<MovedWordViewModel> OpenWordCommand { get; }

    /// <summary>The earlier Assessment placed in the matrix, for the "what is" side.</summary>
    public CompareViewModel Before { get; } = new();

    /// <summary>The later Assessment placed in the matrix, for the "what could be" side.</summary>
    public CompareViewModel After { get; } = new();

    /// <summary>Every move, worst first; a move is all the words that went from one cell to the same other cell.</summary>
    public ObservableCollection<MoveViewModel> Moves { get; } = [];

    /// <summary>The words of the chosen move.</summary>
    public ObservableCollection<MovedWordViewModel> Words { get; } = [];

    public IRelayCommand ClearCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChosenMove))]
    private MoveViewModel? _selectedMove;

    public bool HasChosenMove => SelectedMove is not null;

    [ObservableProperty]
    private string _beforeLabel = string.Empty;

    [ObservableProperty]
    private string _afterLabel = string.Empty;

    public bool HasDifference => Moves.Count > 0;

    public int ComparedCount { get; private set; }
    public int MovedCount { get; private set; }
    public int RegressedCount { get; private set; }
    public int OnlyInOneCount { get; private set; }

    public string Summary => !HasDifference ? string.Empty
        : MovedCount == 0 ? $"None of the {ComparedCount:N0} words changed cell."
        : $"{MovedCount:N0} of {ComparedCount:N0} words changed cell"
          + (RegressedCount > 0 ? $"; {RegressedCount:N0} regressed." : "; none regressed.");

    public string OnlyInOneText => OnlyInOneCount == 0 ? string.Empty
        : $"{OnlyInOneCount:N0} word{(OnlyInOneCount == 1 ? " was" : "s were")} in only one run and {(OnlyInOneCount == 1 ? "is" : "are")} not compared.";

    partial void OnSelectedMoveChanged(MoveViewModel? value)
    {
        Words.Clear();
        foreach (var word in value?.Words ?? []) Words.Add(word);
        MarkMoveInMatrices(value);
    }

    /// <summary>
    /// Compares <paramref name="before"/> with <paramref name="after"/>, word by word; <see langword="null"/> on either
    /// side clears the view.
    /// </summary>
    public void Load(IReadOnlyList<AssessWordRowViewModel>? before, IReadOnlyList<AssessWordRowViewModel>? after,
        string beforeLabel, string afterLabel)
    {
        Moves.Clear();
        SelectedMove = null;
        Before.Load(before);
        After.Load(after);
        BeforeLabel = beforeLabel;
        AfterLabel = afterLabel;
        (ComparedCount, MovedCount, RegressedCount, OnlyInOneCount) = (0, 0, 0, 0);

        if (before is not null && after is not null)
        {
            var earlier = before.GroupBy(row => row.Word, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var later = after.GroupBy(row => row.Word, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var both = earlier.Keys.Where(later.ContainsKey).ToList();
            ComparedCount = both.Count;
            OnlyInOneCount = earlier.Count + later.Count - 2 * both.Count;

            var moved = both.Select(word => new MovedWordViewModel(earlier[word], later[word]))
                .GroupBy(word => (word.From, word.To))
                .Select(group => new MoveViewModel(group.Key.From, group.Key.To, group.ToList()))
                .OrderBy(move => move.Kind)
                .ThenByDescending(move => move.Count);
            foreach (var move in moved) Moves.Add(move);
            MovedCount = Moves.Where(move => move.Kind != MoveKind.Unchanged).Sum(move => move.Count);
            RegressedCount = Moves.Where(move => move.Kind == MoveKind.Regressed).Sum(move => move.Count);
        }

        OnPropertyChanged(nameof(HasDifference));
        OnPropertyChanged(nameof(ComparedCount));
        OnPropertyChanged(nameof(MovedCount));
        OnPropertyChanged(nameof(RegressedCount));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(OnlyInOneText));
        SelectedMove = Moves.FirstOrDefault(move => move.Kind != MoveKind.Unchanged);
    }

    // The chosen move's cells are outlined in both small matrices, so the move reads as "from here to there".
    private void MarkMoveInMatrices(MoveViewModel? move)
    {
        Before.ClearSelectionCommand.Execute(null);
        After.ClearSelectionCommand.Execute(null);
        if (move is null) return;
        Before.Toggle(Before.Cells.Single(cell => (cell.Row, cell.Column) == move.From), additive: false);
        After.Toggle(After.Cells.Single(cell => (cell.Row, cell.Column) == move.To), additive: false);
    }

    /// <summary>
    /// What moving from <paramref name="from"/> to <paramref name="to"/> means. A timeout is never a verdict, so leaving
    /// one settles a word and entering one only loses certainty; otherwise violations and coverage decide, and any
    /// other move is better or worse by how much the cell keeps of what a person decided.
    /// </summary>
    public static MoveKind KindOf((WordProjectStatus Row, CompareColumn Column) from, (WordProjectStatus Row, CompareColumn Column) to)
    {
        if (from == to) return MoveKind.Unchanged;
        var before = CompareViewModel.MeaningOf(from.Row, from.Column).Family;
        var after = CompareViewModel.MeaningOf(to.Row, to.Column).Family;
        if (after == CompareFamily.Violation && before != CompareFamily.Violation) return MoveKind.Regressed;
        if (before == CompareFamily.New && after == CompareFamily.Nobody) return MoveKind.Regressed;
        if (after == CompareFamily.Unknown && before != CompareFamily.Unknown) return MoveKind.NowUnknown;
        if (before == CompareFamily.Unknown) return after == CompareFamily.Unknown ? MoveKind.Changed : MoveKind.Settled;
        if (before == CompareFamily.Violation) return MoveKind.Fixed;
        if (before == CompareFamily.Nobody && after == CompareFamily.New) return MoveKind.NewCoverage;
        if (from.Row != to.Row && to.Row is WordProjectStatus.Approved or WordProjectStatus.Rejected or WordProjectStatus.IncorrectSpelling)
            return MoveKind.Decided;
        // Within the Candidate row nobody has decided anything, so a different answer there is a change, not a loss.
        if (from.Row == WordProjectStatus.Candidate && to.Row == WordProjectStatus.Candidate) return MoveKind.Changed;
        var gain = Worth(after) - Worth(before);
        return gain > 0 ? MoveKind.Improved : gain < 0 ? MoveKind.Regressed : MoveKind.Changed;
    }

    private static int Worth(CompareFamily family) => family switch
    {
        CompareFamily.Good or CompareFamily.Fine => 4,
        CompareFamily.New => 3,
        CompareFamily.Review => 2,
        CompareFamily.Nobody => 1,
        _ => 0,
    };

    /// <summary>The words a move of <paramref name="kind"/> is listed with.</summary>
    public static string LabelOf(MoveKind kind) => kind switch
    {
        MoveKind.Regressed => "regressed",
        MoveKind.NowUnknown => "now unknown",
        MoveKind.Changed => "changed",
        MoveKind.NewCoverage => "new coverage",
        MoveKind.Fixed => "fixed",
        MoveKind.Decided => "decided",
        MoveKind.Settled => "settled",
        MoveKind.Improved => "improved",
        _ => "unchanged",
    };

    /// <summary>The shared meaning a move is coloured with, so a regression reads like every other bad news.</summary>
    public static Verdict VerdictOf(MoveKind kind) => kind switch
    {
        MoveKind.Regressed => Verdict.Differs,
        MoveKind.NowUnknown or MoveKind.Changed => Verdict.NoResult,
        MoveKind.NewCoverage => Verdict.New,
        MoveKind.Unchanged => Verdict.Limit,
        _ => Verdict.Agrees,
    };
}

/// <summary>All the words that went from one cell to the same other cell between two Assessments.</summary>
public sealed class MoveViewModel
{
    public MoveViewModel((WordProjectStatus Row, CompareColumn Column) from, (WordProjectStatus Row, CompareColumn Column) to,
        IReadOnlyList<MovedWordViewModel> words)
    {
        From = from;
        To = to;
        Words = words.OrderByDescending(word => word.Occurrences ?? 0).ThenBy(word => word.Word, StringComparer.CurrentCulture).ToArray();
        Kind = DifferenceViewModel.KindOf(from, to);
        FromLabel = CompareViewModel.MeaningOf(from.Row, from.Column).Label;
        ToLabel = CompareViewModel.MeaningOf(to.Row, to.Column).Label;
    }

    public (WordProjectStatus Row, CompareColumn Column) From { get; }
    public (WordProjectStatus Row, CompareColumn Column) To { get; }
    public IReadOnlyList<MovedWordViewModel> Words { get; }
    public int Count => Words.Count;
    public string CountText => Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
    public MoveKind Kind { get; }
    public string KindLabel => DifferenceViewModel.LabelOf(Kind);
    public Verdict Verdict => DifferenceViewModel.VerdictOf(Kind);
    public bool IsRegression => Kind == MoveKind.Regressed;

    /// <summary>The cell a word left, with its row, since two rows share cell names such as "Unknown".</summary>
    public string FromLabel { get; }
    public string FromRow => CompareViewModel.RowLabelOf(From.Row);

    public string ToLabel { get; }
    public string ToRow => CompareViewModel.RowLabelOf(To.Row);

    public string AccessibleName => $"{Count} words from {FromRow}, {FromLabel} to {ToRow}, {ToLabel}: {KindLabel}";
}

/// <summary>One word of a move, with what the parser said for it in each run.</summary>
public sealed class MovedWordViewModel
{
    public MovedWordViewModel(AssessWordRowViewModel before, AssessWordRowViewModel after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        Word = after.Word;
        Occurrences = after.OccurrenceCount ?? before.OccurrenceCount;
        From = CompareViewModel.Place(before);
        To = CompareViewModel.Place(after);
        BeforeText = Describe(before);
        AfterText = Describe(after);
    }

    public string Word { get; }
    public int? Occurrences { get; }
    public string OccurrenceText => Occurrences is { } count ? $"×{count}" : "—";
    public (WordProjectStatus Row, CompareColumn Column) From { get; }
    public (WordProjectStatus Row, CompareColumn Column) To { get; }

    /// <summary>What the parser came to in the earlier run: its result, and its readings when it has any.</summary>
    public string BeforeText { get; }

    public string AfterText { get; }

    private static string Describe(AssessWordRowViewModel row) =>
        row.Readings.Count == 0 ? row.Result : $"{row.Result}: {row.ReadingText}";
}
