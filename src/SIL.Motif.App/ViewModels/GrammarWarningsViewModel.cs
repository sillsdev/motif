using System.Collections.ObjectModel;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Which findings the Grammar stage's chips show: all, the ones to fix first, or the rest.</summary>
public enum GrammarFindingBucket
{
    All,
    LeftOut,
    WorthALook,
}

/// <summary>
/// The grammar findings as groups of one kind each, then a table of the chosen kind: one row per finding,
/// sortable by any column and filtered by a separate search per column, all within what is already in memory.
/// </summary>
/// <remarks>
/// <see cref="Rows"/> is the grid's own collection view rather than a list rebuilt on each keystroke, so a
/// sort the person chose by clicking a header survives every change to a filter.
/// </remarks>
public sealed partial class GrammarWarningsViewModel : ObservableObject
{
    private readonly List<GrammarWarningRowViewModel> _all = [];

    public GrammarWarningsViewModel()
    {
        Rows = new DataGridCollectionView(_all) { Filter = Matches };
        SetBucketCommand = new RelayCommand<GrammarFindingBucket>(bucket => Bucket = bucket);
        // Choosing the group already in force clears it, so one control both narrows and widens.
        SelectGroupCommand = new RelayCommand<GrammarFindingGroupViewModel?>(group =>
            SelectedGroup = ReferenceEquals(group, SelectedGroup) ? null : group);
    }

    /// <summary>Kinds of finding whose sentence says the parser left something out of the grammar, largest first.</summary>
    public ObservableCollection<GrammarFindingGroupViewModel> LeftOutGroups { get; } = [];

    /// <summary>The remaining kinds, largest first.</summary>
    public ObservableCollection<GrammarFindingGroupViewModel> WorthALookGroups { get; } = [];

    public bool HasLeftOutGroups => LeftOutGroups.Count > 0 && Bucket != GrammarFindingBucket.WorthALook;
    public bool HasWorthALookGroups => WorthALookGroups.Count > 0 && Bucket != GrammarFindingBucket.LeftOut;

    public int LeftOutCount => _all.Count(row => row.IsLeftOut);
    public int WorthALookCount => _all.Count(row => !row.IsLeftOut);

    public IRelayCommand<GrammarFindingBucket> SetBucketCommand { get; }
    public IRelayCommand<GrammarFindingGroupViewModel?> SelectGroupCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLeftOutGroups))]
    [NotifyPropertyChangedFor(nameof(HasWorthALookGroups))]
    private GrammarFindingBucket _bucket = GrammarFindingBucket.All;

    /// <summary>The kind of finding the table shows, or <see langword="null"/> for the whole bucket.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGroup))]
    [NotifyPropertyChangedFor(nameof(SelectedGroupTitle))]
    private GrammarFindingGroupViewModel? _selectedGroup;

    public bool HasSelectedGroup => SelectedGroup is not null;

    /// <summary>The heading over the table: the chosen kind, or which findings are showing.</summary>
    public string SelectedGroupTitle => SelectedGroup?.Name ?? Bucket switch
    {
        GrammarFindingBucket.LeftOut => "Left out of the grammar",
        GrammarFindingBucket.WorthALook => "Worth a look",
        _ => "Every finding",
    };

    partial void OnBucketChanged(GrammarFindingBucket value)
    {
        if (SelectedGroup is { } group && !InBucket(group.IsLeftOut)) SelectedGroup = null;
        OnPropertyChanged(nameof(SelectedGroupTitle));
        Refresh();
    }

    partial void OnSelectedGroupChanged(GrammarFindingGroupViewModel? value)
    {
        foreach (var group in LeftOutGroups.Concat(WorthALookGroups)) group.IsSelected = ReferenceEquals(group, value);
        Refresh();
    }

    private bool InBucket(bool leftOut) => Bucket switch
    {
        GrammarFindingBucket.LeftOut => leftOut,
        GrammarFindingBucket.WorthALook => !leftOut,
        _ => true,
    };

    private void RebuildGroups()
    {
        LeftOutGroups.Clear();
        WorthALookGroups.Clear();
        foreach (var group in _all.GroupBy(row => (row.GroupName, row.IsLeftOut))
                     .Select(group => new GrammarFindingGroupViewModel(group.Key.GroupName, group.Key.IsLeftOut, group.Count()))
                     .OrderByDescending(group => group.Count).ThenBy(group => group.Name, StringComparer.CurrentCulture))
            (group.IsLeftOut ? LeftOutGroups : WorthALookGroups).Add(group);
        SelectedGroup = null;
        OnPropertyChanged(nameof(HasLeftOutGroups));
        OnPropertyChanged(nameof(HasWorthALookGroups));
        OnPropertyChanged(nameof(LeftOutCount));
        OnPropertyChanged(nameof(WorthALookCount));
    }

    /// <summary>The rows on display: every finding that satisfies all four column filters.</summary>
    public DataGridCollectionView Rows { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAny))]
    [NotifyPropertyChangedFor(nameof(CountSummary))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountSummary))]
    private int _shownCount;

    [ObservableProperty]
    private string _severityFilter = string.Empty;

    [ObservableProperty]
    private string _kindFilter = string.Empty;

    [ObservableProperty]
    private string _whereFilter = string.Empty;

    [ObservableProperty]
    private string _problemFilter = string.Empty;

    public bool HasAny => TotalCount > 0;

    public string CountSummary => ShownCount == TotalCount
        ? $"{TotalCount} finding(s)"
        : $"{ShownCount} of {TotalCount} finding(s) match the filters";

    /// <summary>Replaces every row with <paramref name="warnings"/>, or clears the table for <see langword="null"/>.</summary>
    public void Load(IReadOnlyList<GrammarWarning>? warnings)
    {
        _all.Clear();
        if (warnings is not null) _all.AddRange(warnings.Select(warning => new GrammarWarningRowViewModel(warning)));
        TotalCount = _all.Count;
        RebuildGroups();
        Refresh();
    }

    partial void OnSeverityFilterChanged(string value) => Refresh();
    partial void OnKindFilterChanged(string value) => Refresh();
    partial void OnWhereFilterChanged(string value) => Refresh();
    partial void OnProblemFilterChanged(string value) => Refresh();

    private void Refresh()
    {
        Rows.Refresh();
        ShownCount = _all.Count(row => Matches(row));
    }

    private bool Matches(object item) =>
        item is GrammarWarningRowViewModel row
        && InBucket(row.IsLeftOut)
        && (SelectedGroup is not { } group || (group.Name == row.GroupName && group.IsLeftOut == row.IsLeftOut))
        && Contains(row.Severity, SeverityFilter)
        && Contains(row.Kind, KindFilter)
        && Contains(row.Where, WhereFilter)
        && (Contains(row.Problem, ProblemFilter) || Contains(row.Text, ProblemFilter));

    private static bool Contains(string text, string filter) =>
        string.IsNullOrWhiteSpace(filter) || text.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>One kind of finding in the Grammar stage's list, with how many findings are of that kind.</summary>
public sealed partial class GrammarFindingGroupViewModel : ObservableObject
{
    public GrammarFindingGroupViewModel(string name, bool isLeftOut, int count)
    {
        Name = name;
        IsLeftOut = isLeftOut;
        Count = count;
    }

    public string Name { get; }

    /// <summary>Whether this kind is among the findings where the parser left something out.</summary>
    public bool IsLeftOut { get; }

    public int Count { get; }

    public Verdict Meaning => IsLeftOut ? Verdict.Differs : Verdict.Limit;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// One finding as the grid shows it: the structured parts for display, and each column's plain text for
/// sorting and searching.
/// </summary>
public sealed class GrammarWarningRowViewModel
{
    public GrammarWarningRowViewModel(GrammarWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        Severity = warning.Severity.Length == 0 ? "note" : warning.Severity;
        Kind = warning.Kind;
        SubjectParts = warning.Subject;
        ProblemParts = warning.Problem;
        Where = PlainText(warning.Subject);
        Problem = PlainText(warning.Problem);
        Text = warning.Text;
        GroupName = warning.Group ?? GrammarFindingShapes.LabelOf(warning.Text);
        IsLeftOut = warning.Severity == "error" || GrammarFindingShapes.IsLeftOut(warning.Text);
    }

    /// <summary>The kind of finding this is, by the parser's name for it or else by the shape of its sentence.</summary>
    public string GroupName { get; }

    /// <summary>Whether the parser left something out of the grammar here, or called it an error.</summary>
    public bool IsLeftOut { get; }

    public string Severity { get; }
    public bool IsWarning => Severity == "warning";
    public bool IsCapability => Severity == "capability";
    public string Kind { get; }
    public string Where { get; }
    public string Problem { get; }

    /// <summary>The line exactly as the parser wrote it, identifiers included, for a tooltip or a bug report.</summary>
    public string Text { get; }

    public IReadOnlyList<GrammarWarningPart> SubjectParts { get; }
    public IReadOnlyList<GrammarWarningPart> ProblemParts { get; }

    private static string PlainText(IReadOnlyList<GrammarWarningPart> parts) =>
        string.Join(" ", parts.Select(part => part.Text));
}
