using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The grammar findings from one Assessment as a table: one row per finding, sortable by any column and
/// filtered by a separate search per column, all within what is already in memory.
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
        && Contains(row.Severity, SeverityFilter)
        && Contains(row.Kind, KindFilter)
        && Contains(row.Where, WhereFilter)
        && (Contains(row.Problem, ProblemFilter) || Contains(row.Text, ProblemFilter));

    private static bool Contains(string text, string filter) =>
        string.IsNullOrWhiteSpace(filter) || text.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase);
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
    }

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
