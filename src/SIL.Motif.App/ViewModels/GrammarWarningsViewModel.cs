using System.Collections.ObjectModel;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Which grammar-health report levels the Warnings page displays.</summary>
public enum GrammarFindingBucket
{
    All,
    Errors,
    Warnings,
    Information,
}

/// <summary>The Warnings page groups diagnostics by report level and shows the selected group's rows.</summary>
/// <remarks>
/// <see cref="Rows"/> is the grid's own collection view, so a header sort survives filtering.
/// Buckets come from PanGloss's structured level field, independent of diagnostic wording.
/// </remarks>
public sealed partial class GrammarWarningsViewModel : ObservableObject
{
    private readonly List<GrammarWarningRowViewModel> _all = [];

    public GrammarWarningsViewModel()
    {
        Rows = new DataGridCollectionView(_all) { Filter = Matches };
        SetBucketCommand = new RelayCommand<GrammarFindingBucket>(bucket => Bucket = bucket);
        // Selecting the active group again clears it, so the same control narrows and widens the table.
        SelectGroupCommand = new RelayCommand<GrammarFindingGroupViewModel?>(group =>
            SelectedGroup = ReferenceEquals(group, SelectedGroup) ? null : group);
    }

    public ObservableCollection<GrammarFindingGroupViewModel> WarningGroups { get; } = [];
    public ObservableCollection<GrammarFindingGroupViewModel> ErrorGroups { get; } = [];
    public ObservableCollection<GrammarFindingGroupViewModel> InformationGroups { get; } = [];

    public bool HasErrorGroups => ErrorGroups.Count > 0 && Bucket != GrammarFindingBucket.Information &&
        Bucket != GrammarFindingBucket.Warnings;
    public bool HasWarningGroups => WarningGroups.Count > 0 && Bucket != GrammarFindingBucket.Information &&
        Bucket != GrammarFindingBucket.Errors;
    public bool HasInformationGroups => InformationGroups.Count > 0 && Bucket != GrammarFindingBucket.Errors &&
        Bucket != GrammarFindingBucket.Warnings;

    public int ErrorCount => VisibleFindings()
        .Where(row => row.Level == GrammarDiagnosticLevel.Error).Sum(row => row.RepeatCount);
    public int WarningCount => VisibleFindings()
        .Where(row => row.Level == GrammarDiagnosticLevel.Warning).Sum(row => row.RepeatCount);
    public int InformationCount => VisibleFindings()
        .Where(row => row.Level == GrammarDiagnosticLevel.Information).Sum(row => row.RepeatCount);

    /// <summary>The report's error, warning, and information totals for the page summary.</summary>
    public string BreakdownText => $"{ErrorCount} errors, {WarningCount} warnings, {InformationCount} information findings.";

    public IRelayCommand<GrammarFindingBucket> SetBucketCommand { get; }
    public IRelayCommand<GrammarFindingGroupViewModel?> SelectGroupCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorGroups))]
    [NotifyPropertyChangedFor(nameof(HasWarningGroups))]
    [NotifyPropertyChangedFor(nameof(HasInformationGroups))]
    private GrammarFindingBucket _bucket = GrammarFindingBucket.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGroup))]
    [NotifyPropertyChangedFor(nameof(SelectedGroupTitle))]
    [NotifyPropertyChangedFor(nameof(CountSummary))]
    private GrammarFindingGroupViewModel? _selectedGroup;

    public bool HasSelectedGroup => SelectedGroup is not null;

    public string SelectedGroupTitle => SelectedGroup?.Name ?? Bucket switch
    {
        GrammarFindingBucket.Errors => "Errors",
        GrammarFindingBucket.Warnings => "Warnings",
        GrammarFindingBucket.Information => "Information",
        _ => "Every finding",
    };

    partial void OnBucketChanged(GrammarFindingBucket value)
    {
        if (SelectedGroup is { } group && !InBucket(group.Level)) SelectedGroup = null;
        OnPropertyChanged(nameof(SelectedGroupTitle));
        Refresh();
    }

    partial void OnSelectedGroupChanged(GrammarFindingGroupViewModel? value)
    {
        foreach (var group in ErrorGroups.Concat(WarningGroups).Concat(InformationGroups))
            group.IsSelected = ReferenceEquals(group, value);
        Refresh();
    }

    private bool InBucket(GrammarDiagnosticLevel level) => Bucket switch
    {
        GrammarFindingBucket.Errors => level == GrammarDiagnosticLevel.Error,
        GrammarFindingBucket.Warnings => level == GrammarDiagnosticLevel.Warning,
        GrammarFindingBucket.Information => level == GrammarDiagnosticLevel.Information,
        _ => true,
    };

    private void RebuildGroups()
    {
        ErrorGroups.Clear();
        WarningGroups.Clear();
        InformationGroups.Clear();
        foreach (var group in VisibleFindings()
                     .GroupBy(row => (row.GroupCode, row.GroupName, row.Level))
                     .Select(rows => new GrammarFindingGroupViewModel(
                         rows.Key.GroupCode,
                         rows.Key.GroupName,
                         rows.Key.Level,
                         rows.Sum(row => row.RepeatCount),
                         rows.Select(row => row.Description).FirstOrDefault(text => text.Length > 0),
                         rows.Select(row => row.Guidance).FirstOrDefault(text => text.Length > 0)))
                     .OrderByDescending(group => group.Count)
                     .ThenBy(group => group.Name, StringComparer.CurrentCulture))
            (group.Level switch
            {
                GrammarDiagnosticLevel.Error => ErrorGroups,
                GrammarDiagnosticLevel.Warning => WarningGroups,
                _ => InformationGroups,
            }).Add(group);
        SelectedGroup = null;
        OnPropertyChanged(nameof(HasErrorGroups));
        OnPropertyChanged(nameof(HasWarningGroups));
        OnPropertyChanged(nameof(HasInformationGroups));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(InformationCount));
        OnPropertyChanged(nameof(BreakdownText));
    }

    /// <summary>The diagnostics in the grid's view, preserving its sort when filters change.</summary>
    /// <summary>The diagnostics in the grid's view, preserving its sort when filters change.</summary>
    public DataGridCollectionView Rows { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAny))]
    [NotifyPropertyChangedFor(nameof(CountSummary))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountSummary))]
    private int _shownCount;

    [ObservableProperty]
    private string _whereFilter = string.Empty;

    [ObservableProperty]
    private string _problemFilter = string.Empty;

    public bool HasAny => TotalCount > 0;

    public bool AnyShownWhere => VisibleFindings().Any(row => row.HasWhere && Matches(row));

    public string CountSummary => ShownCount == TotalCount
        ? (TotalCount == 1 ? "1 finding" : $"{TotalCount} findings")
        : SelectedGroup is { } group && ShownCount == group.Count
            ? (ShownCount == 1 ? "1 finding of this kind" : $"{ShownCount} findings of this kind")
            : $"{ShownCount} of {TotalCount} findings match the filters";

    /// <summary>Replaces the displayed report, or clears the page when the check has no report.</summary>
    public void Load(IReadOnlyList<GrammarWarning>? warnings)
    {
        _all.Clear();
        if (warnings is not null)
        {
            _all.AddRange(warnings
            .GroupBy(warning => (warning.Text, warning.Origin, warning.Code, warning.Severity))
                .Select(rows => new GrammarWarningRowViewModel(rows.First(), rows.Count())));
        }
        TotalCount = _all.Sum(row => row.RepeatCount);
        RebuildGroups();
        Refresh();
    }

    partial void OnWhereFilterChanged(string value) => Refresh();
    partial void OnProblemFilterChanged(string value) => Refresh();

    private void Refresh()
    {
        Rows.Refresh();
        ShownCount = VisibleFindings().Where(Matches).Sum(row => row.RepeatCount);
    }

    private bool Matches(object item) =>
        item is GrammarWarningRowViewModel row
        && InBucket(row.Level)
        && (SelectedGroup is not { } group ||
            (group.Code == row.GroupCode && group.Level == row.Level))
        && Contains(row.Where, WhereFilter)
        && (Contains(row.Problem, ProblemFilter) || Contains(row.Text, ProblemFilter));

    private IEnumerable<GrammarWarningRowViewModel> VisibleFindings() => _all;

    private static bool Contains(string text, string filter) =>
        string.IsNullOrWhiteSpace(filter) || text.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase);

}

/// <summary>One report code grouped by name and level in the Warnings page.</summary>
public sealed partial class GrammarFindingGroupViewModel : ObservableObject
{
    public GrammarFindingGroupViewModel(
        string code, string name, GrammarDiagnosticLevel level, int count, string? description = null,
        string? guidance = null)
    {
        Code = code;
        Name = name;
        Level = level;
        Count = count;
        Description = description;
        Guidance = guidance;
    }

    public string Code { get; }
    public string Name { get; }
    /// <summary>The structured error, warning, or information level used to choose its bucket.</summary>
    public GrammarDiagnosticLevel Level { get; }
    public bool IsWarning => Level == GrammarDiagnosticLevel.Warning;
    /// <summary>The number of diagnostics with this code, name, and level.</summary>
    public int Count { get; }
    /// <summary>What this kind means, represented as a favorable or cautionary chip.</summary>
    /// <summary>What the selected diagnostic code means, when PanGloss supplied a description.</summary>
    public string? Description { get; }
    /// <summary>What usually helps with this diagnostic code, when PanGloss supplied guidance.</summary>
    public string? Guidance { get; }
    public bool HasDescription => Description is not null;
    public bool HasGuidance => Guidance is not null;
    public bool HasDetails => HasDescription || HasGuidance;
    public Verdict Meaning => Level is GrammarDiagnosticLevel.Error or GrammarDiagnosticLevel.Warning
        ? Verdict.Differs : Verdict.Limit;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One diagnostic row with its subjects and text ready for display, search, and sorting.</summary>
public sealed class GrammarWarningRowViewModel
{
    public GrammarWarningRowViewModel(GrammarWarning warning, int repeatCount = 1)
    {
        ArgumentNullException.ThrowIfNull(warning);
        RepeatCount = repeatCount;
        Description = warning.Description;
        Guidance = warning.Guidance ?? string.Empty;
        Level = warning.Severity;
        Severity = Level.ToWireValue();
        SubjectParts = warning.Subject;
        ProblemParts = warning.Problem;
        Where = PlainText(warning.Subject);
        Problem = PlainText(warning.Problem);
        Text = warning.Text;
        GroupCode = warning.Code!;
        GroupName = warning.Group!;
        OriginLabel = warning.Origin switch
        {
            GrammarFindingOrigin.Import => "From import",
            GrammarFindingOrigin.Check => "From grammar check",
            _ => string.Empty,
        };
    }

    public string GroupName { get; }
    public string GroupCode { get; }
    public string OriginLabel { get; }
    public bool HasOrigin => OriginLabel.Length > 0;
    public GrammarDiagnosticLevel Level { get; }
    public bool IsWarning => Level == GrammarDiagnosticLevel.Warning;
    public bool IsError => Level == GrammarDiagnosticLevel.Error;
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
    public bool IsInfo => Level == GrammarDiagnosticLevel.Information;
    public string Where { get; }
    public string Problem { get; }
    public string Text { get; }
    public IReadOnlyList<GrammarWarningPart> SubjectParts { get; }
    public IReadOnlyList<GrammarWarningPart> ProblemParts { get; }

    private static string PlainText(IReadOnlyList<GrammarWarningPart> parts) =>
        string.Join(" ", parts.Select(part => part.Text));
}
