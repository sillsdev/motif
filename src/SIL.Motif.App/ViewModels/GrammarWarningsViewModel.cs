using System.Collections.ObjectModel;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Help;

namespace SIL.Motif.App.ViewModels;

/// <summary>Which grammar-health report levels the Warnings page displays.</summary>
public enum GrammarFindingBucket
{
    All,
    Errors,
    Warnings,
    Information,
}

/// <summary>
/// The mark each grammar finding level wears on the Warnings page, on its filter chip and beside each kind's count
/// alike. None of them is a verdict glyph: a grammar warning is not a parse that differs.
/// </summary>
public static class GrammarLevelMarks
{
    public const string Error = "×";
    public const string Warning = "!";
    public const string Information = "i";

    /// <summary>The mark for <paramref name="level"/>.</summary>
    public static string Of(GrammarDiagnosticLevel level) => level switch
    {
        GrammarDiagnosticLevel.Error => Error,
        GrammarDiagnosticLevel.Warning => Warning,
        _ => Information,
    };
}

/// <summary>The Warnings page groups diagnostics by report level and shows the selected group's rows.</summary>
/// <remarks>
/// <see cref="Rows"/> is the grid's own collection view, so a header sort survives filtering.
/// Buckets come from PanGloss's structured level field, independent of diagnostic wording.
/// </remarks>
public sealed partial class GrammarWarningsViewModel : ObservableObject
{
    internal static readonly Lazy<WarningMeanings> DefaultMeanings = new(() => WarningMeanings.Load());
    private readonly List<GrammarWarningRowViewModel> _all = [];
    private readonly WarningMeanings _meanings;

    /// <summary>Builds the page's table, reading each warning's plain meaning from <paramref name="meanings"/>.</summary>
    /// <param name="meanings">The meaning table, or the one for the current UI culture when omitted.</param>
    public GrammarWarningsViewModel(WarningMeanings? meanings = null)
    {
        _meanings = meanings ?? DefaultMeanings.Value;
        Rows = new DataGridCollectionView(_all) { Filter = Matches };
        SetBucketCommand = new RelayCommand<GrammarFindingBucket>(bucket => Bucket = bucket);
        // Selecting the active group again clears it, so the same control narrows and widens the table.
        SelectGroupCommand = new RelayCommand<GrammarFindingGroupViewModel?>(group =>
            SelectedGroup = ReferenceEquals(group, SelectedGroup) ? null : group);
    }

    public ObservableCollection<GrammarFindingGroupViewModel> WarningGroups { get; } = [];
    public ObservableCollection<GrammarFindingGroupViewModel> ErrorGroups { get; } = [];
    public ObservableCollection<GrammarFindingGroupViewModel> InformationGroups { get; } = [];

    public bool HasWarningGroups => WarningGroups.Count > 0 &&
        Bucket is not (GrammarFindingBucket.Errors or GrammarFindingBucket.Information);
    public bool HasInformationGroups => InformationGroups.Count > 0 &&
        Bucket is not (GrammarFindingBucket.Errors or GrammarFindingBucket.Warnings);
    public bool HasErrorGroups => ErrorGroups.Count > 0 &&
        Bucket is not (GrammarFindingBucket.Information or GrammarFindingBucket.Warnings);

    public bool HasReportedErrors => _all.Any(row => row.Level == GrammarDiagnosticLevel.Error);
    public bool HasReportedWarnings => _all.Any(row => row.Level == GrammarDiagnosticLevel.Warning);
    public bool HasReportedInformation => _all.Any(row => row.Level == GrammarDiagnosticLevel.Information);

    public int ErrorCount => VisibleFindings()
        .Where(row => row.Level == GrammarDiagnosticLevel.Error).Sum(row => row.RepeatCount);
    public int WarningCount => VisibleFindings()
        .Where(row => row.Level == GrammarDiagnosticLevel.Warning).Sum(row => row.RepeatCount);
    public int InformationCount => VisibleFindings()
        .Where(row => row.Level == GrammarDiagnosticLevel.Information).Sum(row => row.RepeatCount);

    /// <summary>The report's error, warning, and information totals for the page summary.</summary>
    public string BreakdownText => $"{ErrorCount} {CountWord(ErrorCount, "error", "errors")}, " +
        $"{WarningCount} {CountWord(WarningCount, "warning", "warnings")}, " +
        $"{InformationCount} {CountWord(InformationCount, "information finding", "information findings")}.";

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
                         rows.Select(row => row.HasMeaning ? row.Meaning : row.Description)
                             .FirstOrDefault(text => text.Length > 0),
                         rows.Select(row => row.Guidance).FirstOrDefault(text => text.Length > 0),
                         rows.Any(row => row.HasMeaning)))
                     .OrderByDescending(group => group.Count)
                     .ThenBy(group => group.Name, StringComparer.CurrentCulture))
        {
            var groups = group.Level switch
            {
                GrammarDiagnosticLevel.Error => ErrorGroups,
                GrammarDiagnosticLevel.Warning => WarningGroups,
                _ => InformationGroups,
            };
            groups.Add(group);
        }
        SelectedGroup = null;
        OnPropertyChanged(nameof(HasErrorGroups));
        OnPropertyChanged(nameof(HasWarningGroups));
        OnPropertyChanged(nameof(HasInformationGroups));
        OnPropertyChanged(nameof(HasReportedErrors));
        OnPropertyChanged(nameof(HasReportedWarnings));
        OnPropertyChanged(nameof(HasReportedInformation));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(InformationCount));
        OnPropertyChanged(nameof(BreakdownText));
    }

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

    /// <summary>Whether the rows now shown hold more than one level, so the table needs its Level column.</summary>
    public bool AnyShownLevelsDiffer => VisibleFindings().Where(Matches).Select(row => row.Level).Distinct().Skip(1).Any();

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
                .Select(rows => new GrammarWarningRowViewModel(rows.First(), rows.Count(), _meanings)));
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
        && (Contains(row.Meaning, ProblemFilter) || Contains(row.Problem, ProblemFilter) ||
            Contains(row.Text, ProblemFilter));

    private IEnumerable<GrammarWarningRowViewModel> VisibleFindings() => _all;

    private static bool Contains(string text, string filter) =>
        string.IsNullOrWhiteSpace(filter) || text.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private static string CountWord(int count, string singular, string plural) => count == 1 ? singular : plural;

}

/// <summary>One report code grouped by name and level in the Warnings page.</summary>
public sealed partial class GrammarFindingGroupViewModel : ObservableObject
{
    public GrammarFindingGroupViewModel(
        string code, string name, GrammarDiagnosticLevel level, int count, string? description = null,
        string? guidance = null, bool isKnown = false)
    {
        IsKnown = isKnown;
        Code = code;
        Name = name;
        Level = level;
        Count = count;
        Description = description;
        Guidance = guidance;
    }

    public string Code { get; }
    public string Name { get; }

    /// <summary>Whether Motif's table has a plain meaning for this code, rather than only the parser's words.</summary>
    public bool IsKnown { get; }
    /// <summary>The structured error, warning, or information level used to choose its bucket.</summary>
    public GrammarDiagnosticLevel Level { get; }
    public bool IsWarning => Level == GrammarDiagnosticLevel.Warning;
    public bool IsError => Level == GrammarDiagnosticLevel.Error;
    /// <summary>The number of diagnostics with this code, name, and level.</summary>
    public int Count { get; }
    /// <summary>What the selected diagnostic code means, when PanGloss supplied a description.</summary>
    public string? Description { get; }
    /// <summary>What usually helps with this diagnostic code, when PanGloss supplied guidance.</summary>
    public string? Guidance { get; }
    public bool HasDescription => Description is not null;
    public bool HasGuidance => Guidance is not null;
    public bool HasDetails => HasDescription || HasGuidance;
    /// <summary>Whether a finding signals a difference or an informational limit, which chooses its colour.</summary>
    public Verdict Meaning => IsWarning || IsError ? Verdict.Differs : Verdict.Limit;

    /// <summary>The level's own mark, the one its filter chip wears.</summary>
    public string Mark => GrammarLevelMarks.Of(Level);

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One diagnostic row with its subjects and text ready for display, search, and sorting.</summary>
/// <remarks>
/// The row leads with Motif's plain meaning for the warning's code, and keeps the parser's own sentence to show
/// small beneath it, since that sentence is what an AI Handoff or a bug report quotes.
/// </remarks>
public sealed class GrammarWarningRowViewModel
{
    public GrammarWarningRowViewModel(GrammarWarning warning, int repeatCount = 1, WarningMeanings? meanings = null)
    {
        ArgumentNullException.ThrowIfNull(warning);
        var table = meanings ?? GrammarWarningsViewModel.DefaultMeanings.Value;
        var meaning = table.For(warning.Code, warning.Group);
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
        GroupCode = warning.Code ?? string.Empty;
        GroupName = meaning.Title;
        Meaning = meaning.Meaning ?? string.Empty;
        KindLabel = table.KindLabel(warning.Subject.FirstOrDefault(part => part.FieldWorksKind is { Length: > 0 })?.FieldWorksKind
            ?? (warning.Subject.Count > 0 ? "Unknown" : null));
    }

    /// <summary>The kind's plain title, from Motif's table or, for a code it lacks, the parser's own name.</summary>
    public string GroupName { get; }
    public string GroupCode { get; }
    public GrammarDiagnosticLevel Level { get; }
    public bool IsWarning => Level == GrammarDiagnosticLevel.Warning;
    public bool IsError => Level == GrammarDiagnosticLevel.Error;
    public int RepeatCount { get; }

    /// <summary>How often the parser reported this warning, as the Seen column shows it.</summary>
    public string SeenText => $"{RepeatCount:N0}×";

    /// <summary>What the warning means in plain words, or empty when Motif's table does not know its code.</summary>
    public string Meaning { get; }
    public bool HasMeaning => Meaning.Length > 0;

    /// <summary>The FieldWorks name for the kind of object the warning names, or "Grammar-wide" for none.</summary>
    public string KindLabel { get; }
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
