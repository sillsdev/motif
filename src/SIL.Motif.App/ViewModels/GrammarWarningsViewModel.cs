using System.Collections.ObjectModel;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Which findings the Warnings page shows: all, the ones to fix first, or the rest.</summary>
public enum GrammarFindingBucket
{
    All,
    LeftOut,
    WorthALook,
}

/// <summary>The grammar findings as groups, with the selected group's diagnostics shown in a table.</summary>
public sealed partial class GrammarWarningsViewModel : ObservableObject
{
    private readonly List<GrammarWarningRowViewModel> _all = [];

    public GrammarWarningsViewModel()
    {
        Rows = new DataGridCollectionView(_all) { Filter = Matches };
        SetBucketCommand = new RelayCommand<GrammarFindingBucket>(bucket => Bucket = bucket);
        SelectGroupCommand = new RelayCommand<GrammarFindingGroupViewModel?>(group =>
            SelectedGroup = ReferenceEquals(group, SelectedGroup) ? null : group);
    }

    public ObservableCollection<GrammarFindingGroupViewModel> LeftOutGroups { get; } = [];
    public ObservableCollection<GrammarFindingGroupViewModel> WorthALookGroups { get; } = [];

    public bool HasLeftOutGroups => LeftOutGroups.Count > 0 && Bucket != GrammarFindingBucket.WorthALook;
    public bool HasWorthALookGroups => WorthALookGroups.Count > 0 && Bucket != GrammarFindingBucket.LeftOut;

    public int LeftOutCount => VisibleFindings().Where(row => row.IsLeftOut).Sum(row => row.RepeatCount);
    public int WorthALookCount => VisibleFindings().Where(row => !row.IsLeftOut).Sum(row => row.RepeatCount);

    /// <summary>The split between findings that need attention and findings worth reviewing.</summary>
    public string BreakdownText => $"{LeftOutCount} left out of the grammar, {WorthALookCount} worth a look.";

    public IRelayCommand<GrammarFindingBucket> SetBucketCommand { get; }
    public IRelayCommand<GrammarFindingGroupViewModel?> SelectGroupCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLeftOutGroups))]
    [NotifyPropertyChangedFor(nameof(HasWorthALookGroups))]
    private GrammarFindingBucket _bucket = GrammarFindingBucket.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGroup))]
    [NotifyPropertyChangedFor(nameof(SelectedGroupTitle))]
    [NotifyPropertyChangedFor(nameof(CountSummary))]
    private GrammarFindingGroupViewModel? _selectedGroup;

    public bool HasSelectedGroup => SelectedGroup is not null;

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
        foreach (var group in LeftOutGroups.Concat(WorthALookGroups))
            group.IsSelected = ReferenceEquals(group, value);
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
        foreach (var group in VisibleFindings()
                     .GroupBy(row => (row.GroupCode, row.GroupName, row.IsLeftOut))
                     .Select(rows => new GrammarFindingGroupViewModel(
                         rows.Key.GroupCode,
                         rows.Key.GroupName,
                         rows.Key.IsLeftOut,
                         rows.Sum(row => row.RepeatCount),
                         rows.Select(row => row.Description).FirstOrDefault(text => text.Length > 0),
                         rows.Select(row => row.Guidance).FirstOrDefault(text => text.Length > 0)))
                     .OrderByDescending(group => group.Count)
                     .ThenBy(group => group.Name, StringComparer.CurrentCulture))
            (group.IsLeftOut ? LeftOutGroups : WorthALookGroups).Add(group);
        SelectedGroup = null;
        OnPropertyChanged(nameof(HasLeftOutGroups));
        OnPropertyChanged(nameof(HasWorthALookGroups));
        OnPropertyChanged(nameof(LeftOutCount));
        OnPropertyChanged(nameof(WorthALookCount));
        OnPropertyChanged(nameof(BreakdownText));
    }

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

    /// <summary>Whether any visible finding names a subject for the Where column.</summary>
    public bool AnyShownWhere => VisibleFindings().Any(row => row.HasWhere && Matches(row));

    public string CountSummary
    {
        get
        {
            var shown = ShownCount == TotalCount
                ? (TotalCount == 1 ? "1 finding" : $"{TotalCount} findings")
                : SelectedGroup is { } group && ShownCount == group.Count
                    ? (ShownCount == 1 ? "1 finding of this kind" : $"{ShownCount} findings of this kind")
                    : $"{ShownCount} of {TotalCount} findings match the filters";
            return shown;
        }
    }

    /// <summary>Replaces the displayed report, or clears it for <see langword="null"/>.</summary>
    /// <param name="warnings">The report findings to display.</param>
    /// <param name="summary">The report summary used to match findings to display groups.</param>
    public void Load(
        IReadOnlyList<GrammarWarning>? warnings,
        IReadOnlyList<GrammarWarningSummary>? summary = null)
    {
        _all.Clear();
        if (warnings is not null)
        {
            _all.AddRange(warnings
                .GroupBy(warning => (warning.Text, warning.Origin, warning.Code),
                    StringTupleComparer.Instance)
                .Select(rows => new GrammarWarningRowViewModel(
                    rows.First(), rows.Sum(_ => 1), summary?.FirstOrDefault(row => row.Code == rows.Key.Code))));
        }
        TotalCount = _all.Sum(row => row.RepeatCount);
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
        ShownCount = VisibleFindings().Where(Matches).Sum(row => row.RepeatCount);
    }

    private bool Matches(object item) =>
        item is GrammarWarningRowViewModel row
        && InBucket(row.IsLeftOut)
        && (SelectedGroup is not { } group ||
            (group.Code == row.GroupCode && group.IsLeftOut == row.IsLeftOut))
        && Contains(row.Severity, SeverityFilter)
        && Contains(row.Kind, KindFilter)
        && Contains(row.Where, WhereFilter)
        && (Contains(row.Problem, ProblemFilter) || Contains(row.Guidance, ProblemFilter) ||
            Contains(row.Text, ProblemFilter));

    private IEnumerable<GrammarWarningRowViewModel> VisibleFindings() => _all;

    private static bool Contains(string text, string filter) =>
        string.IsNullOrWhiteSpace(filter) || text.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private sealed class StringTupleComparer :
        IEqualityComparer<(string Text, string Origin, string? Code)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals((string Text, string Origin, string? Code) left,
            (string Text, string Origin, string? Code) right) =>
            string.Equals(left.Text, right.Text, StringComparison.Ordinal) &&
            string.Equals(left.Origin, right.Origin, StringComparison.Ordinal) &&
            string.Equals(left.Code, right.Code, StringComparison.Ordinal);

        public int GetHashCode((string Text, string Origin, string? Code) value) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.Text),
                StringComparer.Ordinal.GetHashCode(value.Origin),
                value.Code is null ? 0 : StringComparer.Ordinal.GetHashCode(value.Code));
    }
}

/// <summary>One kind of diagnostic in the Warnings page's group list.</summary>
public sealed partial class GrammarFindingGroupViewModel : ObservableObject
{
    public GrammarFindingGroupViewModel(
        string code, string name, bool isLeftOut, int count, string? description = null, string? guidance = null)
    {
        Code = code;
        Name = name;
        IsLeftOut = isLeftOut;
        Count = count;
        Description = description;
        Guidance = guidance;
    }

    public string Code { get; }
    public string Name { get; }
    public bool IsLeftOut { get; }
    public int Count { get; }
    public string? Description { get; }
    public string? Guidance { get; }
    public bool HasDescription => Description is not null;
    public bool HasGuidance => Guidance is not null;
    public Verdict Meaning => IsLeftOut ? Verdict.Differs : Verdict.Limit;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One diagnostic row with its subjects and text ready for display, search, and sorting.</summary>
public sealed class GrammarWarningRowViewModel
{
    public GrammarWarningRowViewModel(GrammarWarning warning, int repeatCount = 1,
        GrammarWarningSummary? summary = null)
    {
        ArgumentNullException.ThrowIfNull(warning);
        RepeatCount = repeatCount;
        Description = warning.Description;
        Guidance = warning.Guidance ?? string.Empty;
        Severity = warning.Severity.Length == 0 ? "note" : warning.Severity;
        Kind = warning.Kind;
        SubjectParts = warning.Subject;
        ProblemParts = warning.Problem;
        Where = PlainText(warning.Subject);
        Problem = PlainText(warning.Problem);
        Text = warning.Text;
        GroupCode = summary?.Code ?? warning.Code ?? warning.Group ?? GrammarFindingShapes.LabelOf(warning.Text);
        GroupName = summary?.GroupName ?? warning.Group ?? GrammarFindingShapes.LabelOf(warning.Text);
        IsLeftOut = GrammarFindingShapes.IsLeftOut(warning.Text);
        OriginLabel = warning.Origin switch
        {
            "import" => "From import",
            "check" => "From grammar check",
            _ => string.Empty,
        };
    }

    public string GroupName { get; }
    public string GroupCode { get; }
    /// <summary>Where this finding came from, or empty when its source is unknown.</summary>
    public string OriginLabel { get; }

    public bool HasOrigin => OriginLabel.Length > 0;

    /// <summary>Whether the parser left something out of the grammar here, or called it an error.</summary>
    public bool IsLeftOut { get; }
    public int RepeatCount { get; }
    public string RepeatText => RepeatCount switch
    {
        1 => string.Empty,
        2 => "reported twice",
        _ => $"reported {RepeatCount} times",
    };
    public bool IsRepeated => RepeatCount > 1;
    public bool HasWhere => SubjectParts.Count > 0;
    public string Description { get; }
    public string Guidance { get; }
    public string Severity { get; }
    public bool IsWarning => Severity == "warning";
    public bool IsInfo => Severity == "info";
    public string Kind { get; }
    public string Where { get; }
    public string Problem { get; }
    public bool HasGuidance => Guidance.Length > 0;
    public string Text { get; }
    public IReadOnlyList<GrammarWarningPart> SubjectParts { get; }
    public IReadOnlyList<GrammarWarningPart> ProblemParts { get; }

    private static string PlainText(IReadOnlyList<GrammarWarningPart> parts) =>
        string.Join(" ", parts.Select(part => part.Text));
}
