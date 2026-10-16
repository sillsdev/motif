using System.Collections.ObjectModel;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.App.ViewModels;

internal static class WarningFindingIdentity
{
    /// <summary>Returns the stable kind key used to keep one row per reported kind.</summary>
    public static string? ForGrouping(GrammarWarning warning)
    {
        var code = First(warning.Code, warning.CodeLabel, warning.Title, warning.Group);
        return code;
    }

    private static string? First(params string?[] values) => values.FirstOrDefault(value =>
        !string.IsNullOrWhiteSpace(value));
}

/// <summary>One distinct report detail inside a row that groups the same warning kind.</summary>
public sealed class GrammarWarningDetailViewModel
{
    public GrammarWarningDetailViewModel(GrammarWarning warning, int repeatCount)
    {
        Warning = warning;
        RepeatCount = repeatCount;
        var text = PanGlossWarningText.From(warning);
        Title = text.Title;
        Explanation = text.Explanation;
        Guidance = text.Guidance;
        Description = text.Description;
        HelpUrl = text.HelpUrl;
        SubjectParts = warning.Subject.Where(part => part.Role != GrammarWarningPartRole.Object ||
            part.FieldWorksKind != "LexEntry" || part.FieldWorksLink is not { Length: > 0 }).ToArray();
        ProblemParts = warning.Problem;
        HasProblemParts = ProblemParts.Count > 0 &&
            (ProblemParts.Count != 1 || ProblemParts[0].Role != GrammarWarningPartRole.Text ||
                !string.Equals(ProblemParts[0].Text, Description, StringComparison.Ordinal));
        EntryLinks = warning.Subject
            .Where(part => part.Role == GrammarWarningPartRole.Object && part.FieldWorksKind == "LexEntry" &&
                part.FieldWorksLink is { Length: > 0 })
            .Select(part => new GrammarWarningEntryLink(part.Title ?? part.Text,
                FieldWorksLinks.ToolName(part.FieldWorksTool ?? FieldWorksLinks.ToolOf(part.FieldWorksLink!)),
                new Uri(part.FieldWorksLink!)))
            .ToArray();
    }

    /// <summary>One detail for every finding of a kind that repeats once per wordform, listing the wordforms.</summary>
    internal GrammarWarningDetailViewModel(IReadOnlyList<GrammarWarning> warnings, string wordformsText)
        : this(warnings[0], warnings.Count)
    {
        WordformsText = wordformsText;
        SubjectParts = [];
        ProblemParts = [];
        HasProblemParts = false;
        EntryLinks = [];
        Description = string.Empty;
    }

    public GrammarWarning Warning { get; }
    public int RepeatCount { get; }
    public string WordformsText { get; } = string.Empty;
    public bool HasWordforms => WordformsText.Length > 0;
    public string Title { get; }
    public string Explanation { get; }
    public string Guidance { get; }
    public string Description { get; private set; }
    public Uri? HelpUrl { get; }
    public IReadOnlyList<GrammarWarningPart> SubjectParts { get; private set; }
    public IReadOnlyList<GrammarWarningPart> ProblemParts { get; private set; }
    public bool HasProblemParts { get; private set; }
    public IReadOnlyList<GrammarWarningEntryLink> EntryLinks { get; private set; }
    public bool HasEntryLinks => EntryLinks.Count > 0;
    public bool HasExplanation => Explanation.Length > 0;
    public bool HasGuidance => Guidance.Length > 0;
    public bool HasDescription => Description.Length > 0;
    public bool HasHelp => HelpUrl is not null;
    public string SeenText => RepeatCount > 1 ? $"{RepeatCount:N0} findings" : string.Empty;
    public bool HasSeenText => RepeatCount > 1;
}

/// <summary>A named FieldWorks entry link supplied by PanGloss.</summary>
public sealed record GrammarWarningEntryLink(string Entry, string Tool, Uri Address)
{
    public string Label => $"Open {Entry} in {Tool} ↗";
}

/// <summary>Which grammar-health report levels the Warnings page displays.</summary>
public enum GrammarFindingBucket
{
    All,
    Errors,
    Warnings,
    Information,
}

/// <summary>
/// The mark each grammar finding level wears on its filter chip and row. These marks do not replace PanGloss's
/// outcome marks in a word row.
/// </summary>
public static class GrammarLevelMarks
{
    public const string Error = "!";
    public const string Warning = "⚠";
    public const string Information = "i";

    /// <summary>The mark for <paramref name="level"/>.</summary>
    public static string Of(GrammarDiagnosticLevel level) => level switch
    {
        GrammarDiagnosticLevel.Error => Error,
        GrammarDiagnosticLevel.Warning => Warning,
        _ => Information,
    };
}

/// <summary>The Warnings page shows PanGloss findings and the words the stored evidence can attribute.</summary>
public sealed partial class GrammarWarningsViewModel : ObservableObject
{
    private readonly List<GrammarWarningRowViewModel> _all = [];
    private GrammarWarningRowViewModel? _selectedWarning;
    private Func<GrammarWarningRowViewModel, Task>? _reparseWords;
    private Action<GrammarWarningRowViewModel>? _handOffWords;
    private WordRowRoutes? _wordRoutes;

    public GrammarWarningsViewModel()
    {
        Rows = new DataGridCollectionView(_all) { Filter = Matches };
        SetBucketCommand = new RelayCommand<GrammarFindingBucket>(bucket => Bucket = bucket);
        SetReportOrderCommand = new RelayCommand(() => MostYourWordsFirst = false);
        SetMostYourWordsFirstCommand = new RelayCommand(() => MostYourWordsFirst = true);
        OpenDetailsCommand = new RelayCommand<GrammarWarningRowViewModel>(row =>
        {
            if (row is null) return;
            SelectedTableEntry = row;
            row.IsOpen = true;
        });
        HandOffCommand = new RelayCommand<GrammarWarningRowViewModel>(row =>
        {
            if (row is not null) _handOffWords?.Invoke(row);
        });
        ReparseCommand = new AsyncRelayCommand<GrammarWarningRowViewModel>(row =>
            row is not null && row.CanParseAgain && _reparseWords is not null
                ? _reparseWords(row)
                : Task.CompletedTask,
            row => row is not null && row.CanParseAgain && _reparseWords is not null);
    }

    public bool HasReportedErrors => VisibleFindings().Any(row => row.Level == GrammarDiagnosticLevel.Error);
    public bool HasReportedWarnings => VisibleFindings().Any(row => row.Level == GrammarDiagnosticLevel.Warning);
    public bool HasReportedInformation => VisibleFindings().Any(row => row.Level == GrammarDiagnosticLevel.Information);

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
    /// <summary>Changes the warning list to report order.</summary>
    public IRelayCommand SetReportOrderCommand { get; }
    /// <summary>Changes the warning list to put findings with more exact word uses first.</summary>
    public IRelayCommand SetMostYourWordsFirstCommand { get; }
    /// <summary>Selects a finding and opens its page-owned detail panel.</summary>
    public IRelayCommand<GrammarWarningRowViewModel> OpenDetailsCommand { get; }
    public IRelayCommand<GrammarWarningRowViewModel> HandOffCommand { get; }
    public IAsyncRelayCommand<GrammarWarningRowViewModel> ReparseCommand { get; }

    public void ConfigureActions(Action<GrammarWarningRowViewModel> handOffWords,
        Func<GrammarWarningRowViewModel, Task> reparseWords, WordRowRoutes wordRoutes)
    {
        _handOffWords = handOffWords;
        _reparseWords = reparseWords;
        _wordRoutes = wordRoutes;
        ReparseCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    private GrammarFindingBucket _bucket = GrammarFindingBucket.All;

    partial void OnBucketChanged(GrammarFindingBucket value) => Refresh();

    private bool InBucket(GrammarDiagnosticLevel level) => Bucket switch
    {
        GrammarFindingBucket.Errors => level == GrammarDiagnosticLevel.Error,
        GrammarFindingBucket.Warnings => level == GrammarDiagnosticLevel.Warning,
        GrammarFindingBucket.Information => level == GrammarDiagnosticLevel.Information,
        _ => true,
    };

    /// <summary>The findings in the page's filtered view.</summary>
    public DataGridCollectionView Rows { get; }

    /// <summary>The page's area headings and warning rows, in their displayed order.</summary>
    public ObservableCollection<object> TableEntries { get; } = [];

    /// <summary>The selected table row, or null when no finding row is selected.</summary>
    [ObservableProperty]
    private object? _selectedTableEntry;

    /// <summary>The selected finding as a list of at most one, which is all the details pane binds to.</summary>
    public ObservableCollection<GrammarWarningRowViewModel> SelectedDetails { get; } = [];

    partial void OnSelectedTableEntryChanged(object? value)
    {
        if (value is DenseTableGroup)
        {
            SelectedTableEntry = null;
            return;
        }

        if (_selectedWarning is { } previous && !ReferenceEquals(previous, value)) previous.IsOpen = false;
        _selectedWarning = value as GrammarWarningRowViewModel;
        SelectedDetails.Clear();
        if (_selectedWarning is not null) SelectedDetails.Add(_selectedWarning);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAny))]
    [NotifyPropertyChangedFor(nameof(CountSummary))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountSummary))]
    private int _shownCount;

    [ObservableProperty]
    private bool _touchYourWords;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel))]
    private bool _mostYourWordsFirst = true;

    public bool HasAny => _all.Count > 0;

    public string CountSummary => ShownCount == TotalCount
        ? (TotalCount == 1 ? "1 finding" : $"{TotalCount} findings")
        : $"{ShownCount} of {TotalCount} findings match the filters";

    /// <summary>The distinct Selection words reached by exact identity from the findings, when available.</summary>
    public string TouchYourWordsText => WarningWordsQuery.Touched(Findings) is { } touched
        ? $"Touch your words · {touched.Words:N0} {(touched.Words == 1 ? "word" : "words")}" : "Touch your words";

    /// <summary>The current ordering shown on the sort control.</summary>
    public string SortLabel => MostYourWordsFirst ? "Sort: most of your words first" : "Sort: report order";

    /// <summary>The complete projected findings, including the effective words each finding touches.</summary>
    public IReadOnlyList<GrammarWarning> Findings { get; private set; } = [];

    /// <summary>Replaces the displayed report, or clears the page when the check has no report.</summary>
    public void Load(IReadOnlyList<GrammarWarning>? warnings, bool preserveResolved = false)
    {
        Findings = warnings ?? [];
        OnPropertyChanged(nameof(Findings));
        OnPropertyChanged(nameof(TouchYourWordsText));
        var previous = _all.ToArray();
        var next = new List<GrammarWarningRowViewModel>();
        if (warnings is not null)
        {
            var identified = warnings.Select((warning, index) =>
                (Warning: warning, Index: index, Identity: WarningFindingIdentity.ForGrouping(warning)));
            foreach (var group in identified.GroupBy(item =>
                         (item.Identity, UnidentifiedIndex: item.Identity is null ? item.Index : -1)))
            {
                next.Add(new GrammarWarningRowViewModel(group.Select(item => item.Warning).ToArray(), _wordRoutes)
                {
                    SourceOrder = next.Count,
                });
            }
        }
        var resolved = new List<GrammarWarningRowViewModel>();
        if (preserveResolved)
        {
            var current = next.Select(row => row.FindingIdentity).ToHashSet(StringComparer.Ordinal);
            foreach (var row in previous.Where(row => !row.HasRefreshStatus))
            {
                if (row.FindingIdentity is null) row.MarkNotMatched();
                else if (current.Contains(row.FindingIdentity)) continue;
                else row.MarkGone();
                resolved.Add(row);
            }
        }

        _all.Clear();
        _all.AddRange(next);
        _all.AddRange(resolved);
        ApplySort();
        TotalCount = next.Sum(row => row.RepeatCount);
        OnPropertyChanged(nameof(HasReportedErrors));
        OnPropertyChanged(nameof(HasReportedWarnings));
        OnPropertyChanged(nameof(HasReportedInformation));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(InformationCount));
        OnPropertyChanged(nameof(BreakdownText));
        OnPropertyChanged(nameof(HasAny));
        Refresh();
    }

    partial void OnTouchYourWordsChanged(bool value) => Refresh();
    partial void OnMostYourWordsFirstChanged(bool value)
    {
        ApplySort();
        Refresh();
    }

    private void ApplySort()
    {
        var sorted = MostYourWordsFirst
            ? _all.OrderBy(row => row.IsGone).ThenByDescending(row => row.YourWordsCount ?? -1)
                .ThenBy(row => row.SourceOrder).ToArray()
            : _all.OrderBy(row => row.IsGone).ThenBy(row => row.SourceOrder).ToArray();
        _all.Clear();
        _all.AddRange(sorted);
    }

    private void Refresh()
    {
        Rows.Refresh();
        RefreshTableEntries();
        ShownCount = VisibleFindings().Where(Matches).Sum(row => row.RepeatCount);
    }

    private void RefreshTableEntries()
    {
        var groups = Rows.Cast<object>().OfType<GrammarWarningRowViewModel>()
            .GroupBy(WarningArea)
            .OrderBy(group => AreaOrder(group.Key))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        TableEntries.Clear();
        foreach (var group in groups)
        {
            var count = group.Sum(row => row.RepeatCount);
            TableEntries.Add(new DenseTableGroup(group.Key, $"{count:N0} {(count == 1 ? "finding" : "findings")}"));
            foreach (var row in group) TableEntries.Add(row);
        }

        if (SelectedTableEntry is not null && !TableEntries.Contains(SelectedTableEntry))
            SelectedTableEntry = null;
    }

    private static string WarningArea(GrammarWarningRowViewModel row)
    {
        var kinds = row.Warning.Subject.Select(part => part.FieldWorksKind).Where(kind => kind is not null)
            .Cast<string>().ToArray();
        var code = row.Warning.Code ?? string.Empty;
        if (kinds.Contains("PhEnvironment", StringComparer.Ordinal) ||
            code.Contains("environment", StringComparison.OrdinalIgnoreCase))
            return "Environments";
        if (kinds.Any(kind => kind.StartsWith("MoAffix", StringComparison.Ordinal)) ||
            code.Contains("affix", StringComparison.OrdinalIgnoreCase))
            return "Affixes";
        if (kinds.Any(kind => kind.StartsWith("Ph", StringComparison.Ordinal)) ||
            code.Contains("phon", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("rule", StringComparison.OrdinalIgnoreCase))
            return "Phonological rules";
        return "Other findings";
    }

    private static int AreaOrder(string area) => area switch
    {
        "Environments" => 0,
        "Phonological rules" => 1,
        "Affixes" => 2,
        _ => 3,
    };

    private bool Matches(object item) =>
        item is GrammarWarningRowViewModel row
        && InBucket(row.Level)
        && (!TouchYourWords || row.AttributionState == WarningDisplayState.ExactUses);

    private IEnumerable<GrammarWarningRowViewModel> VisibleFindings() => _all.Where(row => !row.IsGone);

    private static string CountWord(int count, string singular, string plural) => count == 1 ? singular : plural;

}

/// <summary>One PanGloss warning kind with its grouped details and combined word evidence.</summary>
public sealed partial class GrammarWarningRowViewModel : ObservableObject
{
    public GrammarWarningRowViewModel(GrammarWarning warning, int repeatCount = 1, WordRowRoutes? wordRoutes = null)
        : this(Enumerable.Repeat(warning, repeatCount).ToArray(), wordRoutes)
    {
    }

    internal GrammarWarningRowViewModel(IReadOnlyList<GrammarWarning> warnings, WordRowRoutes? wordRoutes = null)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        if (warnings.Count == 0) throw new ArgumentException("A kind row needs at least one finding.", nameof(warnings));
        var warning = warnings[0];
        var panGloss = PanGlossWarningText.From(warning);
        Findings = warnings;
        Details = DetailsFor(warnings);
        Warning = warning;
        RepeatCount = warnings.Count;
        Level = warning.Severity;
        Severity = Level.ToWireValue();
        SubjectParts = warnings.SelectMany(item => item.Subject).Distinct().ToArray();
        ProblemParts = warnings.SelectMany(item => item.Problem).Distinct().ToArray();
        Text = string.Join(Environment.NewLine, warnings.Select(item => item.Text).Distinct(StringComparer.Ordinal));
        Message = string.Join(Environment.NewLine, Details.Select(detail => detail.Description)
            .Where(description => description.Length > 0).Distinct(StringComparer.Ordinal));
        PanGlossTitle = panGloss.Title;
        PanGlossExplanation = string.Join(Environment.NewLine, Details.Select(detail => detail.Explanation)
            .Where(explanation => explanation.Length > 0).Distinct(StringComparer.Ordinal));
        PanGlossGuidance = string.Join(Environment.NewLine, Details.Select(detail => detail.Guidance)
            .Where(guidance => guidance.Length > 0).Distinct(StringComparer.Ordinal));
        PanGlossFieldWorksPlaces = panGloss.FieldWorksPlaces;
        PanGlossHelp = panGloss.HelpBody;
        PanGlossReference = panGloss.HelpUrl;
        GroupCode = warning.Code ?? warning.CodeLabel;
        GroupName = panGloss.Title;
        FindingIdentity = WarningFindingIdentity.ForGrouping(warning);
        YourWords = warning.YourWords;
        var namedFindings = warnings.Where(item => item.NamesItem).ToArray();
        var touched = WarningWordsQuery.Touched(namedFindings);
        var evidence = namedFindings.Select(item => item.YourWords).OfType<WarningWords>().ToArray();
        var exactWords = evidence.Where(item => item.Match == WarningWordsMatch.Identity).SelectMany(item => item.Words);
        var membershipWords = evidence.SelectMany(item =>
            (item.Match == WarningWordsMatch.Membership ? item.Words : []).Concat(item.MembershipCandidates));
        var spellingWords = evidence.SelectMany(item =>
            (item.Match == WarningWordsMatch.Spelling ? item.Words : []).Concat(item.SpellingCandidates));
        WordRows = [.. exactWords.DistinctBy(word => word.Row.Word, StringComparer.Ordinal)
            .Select(word => new WordRowViewModel(word.Row, wordRoutes))];
        MembershipCandidateRows = [.. membershipWords.DistinctBy(word => word.Row.Word, StringComparer.Ordinal)
            .Select(word => new WordRowViewModel(word.Row, wordRoutes))];
        SpellingCandidateRows = [.. spellingWords.DistinctBy(word => word.Row.Word, StringComparer.Ordinal)
            .Select(word => new WordRowViewModel(word.Row, wordRoutes))];
        var namedParts = namedFindings.SelectMany(item => item.Subject)
            .Where(part => part.Role is GrammarWarningPartRole.Object or GrammarWarningPartRole.Missing)
            .ToArray();
        var linkedNames = namedParts.Where(part => part.FieldWorksLink is { Length: > 0 }).ToArray();
        var routedNames = namedParts.Where(part => part.Reach is not null).ToArray();
        var displayParts = linkedNames.Length > 0 ? linkedNames : routedNames.Length > 0 ? routedNames : namedParts;
        var displayNames = displayParts.Select(part => part.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text)).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        NamedItemText = displayNames.Length == 1 ? displayNames[0] : string.Empty;
        IsPartialReach = namedFindings.Any(WarningAttribution.HasUnfollowedConnections);
        AttributionState = AggregateAttributionState(namedFindings, touched);
        ReachSummaryText = SummaryFor(namedFindings, touched);
        ToggleOpenCommand = new RelayCommand(() => IsOpen = !IsOpen);
    }

    public GrammarWarning Warning { get; }
    public IReadOnlyList<GrammarWarning> Findings { get; }
    public IReadOnlyList<GrammarWarningDetailViewModel> Details { get; }
    public WarningWords? YourWords { get; }
    public IReadOnlyList<WordRowViewModel> WordRows { get; }
    public IReadOnlyList<WordRowViewModel> MembershipCandidateRows { get; }
    public IReadOnlyList<WordRowViewModel> SpellingCandidateRows { get; }
    /// <summary>The first subject name supplied by PanGloss, when the finding names an item.</summary>
    public string NamedItemText { get; }
    /// <summary>The heading for words whose identity matches the named item.</summary>
    public string ExactWordsHeading => NamedItemText.Length > 0 ? $"Your words that use {NamedItemText}" : "Your words";
    /// <summary>Whether the finding names an item with no exact word uses in the Selection.</summary>
    public bool HasNoExactUses => AttributionState == WarningDisplayState.NoneInSelection;
    public string ReachSummaryText { get; }
    /// <summary>Whether a separate reach explanation is needed for this finding.</summary>
    public bool HasReachStateText => !HasNoExactUses || NamedItemText.Length == 0;
    /// <summary>The plain language shown when no Selection word uses the named item.</summary>
    public string NoExactUsesText => NamedItemText.Length > 0
        ? $"None of your words use {NamedItemText}" : "None of your words use the named items";
    /// <summary>The quiet row title, with repeated identical findings counted once.</summary>
    public string RowTitleText => $"{PanGlossTitle} · {RepeatCount:N0} {CountWord(RepeatCount, "finding", "findings")}";
    /// <summary>Whether the table lets a person select this row; a heading cannot be selected, a finding can.</summary>
    public bool IsSelectable => true;
    /// <summary>The severity written out for the table's first column: Error, Warning or Note.</summary>
    public string SeverityLabel => Level switch
    {
        GrammarDiagnosticLevel.Error => "Error",
        GrammarDiagnosticLevel.Warning => "Warning",
        _ => "Note",
    };
    /// <summary>
    /// The named item for the table's Item column, or the finding's title when it names none, with a count when
    /// identical findings share the row so the row total matches the group's count.
    /// </summary>
    public string TableItemText => (NamedItemText.Length > 0 ? NamedItemText : PanGlossTitle) +
        (RepeatCount > 1 ? $" · {RepeatCount:N0} findings" : string.Empty);
    /// <summary>The PanGloss description for the table's problem column, with raw identifiers shown as the item's name.</summary>
    public string TableProblemText => RawIdentifier.Replace(
        Warning.Description.Length > 0 ? Warning.Description : Message,
        NamedItemText.Length > 0 ? NamedItemText : "an unnamed item");
    private static readonly System.Text.RegularExpressions.Regex RawIdentifier = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    /// <summary>The Named words cell: an exact count, none, or a dash when no exact count is available.</summary>
    public string TableNamedWordsText => WarningWordsQuery.Touched(Findings.Where(item => item.NamesItem).ToArray())
        is not { IsComplete: true } ? "—" : YourWordsCount is { } count
        ? count == 0 ? "none" : $"{count:N0} {(count == 1 ? "word" : "words")}"
        : AttributionState == WarningDisplayState.NoneInSelection ? "none" : "—";
    /// <summary>Whether the separate title says more than the row heading.</summary>
    public bool HasDistinctPanGlossTitle => PanGlossTitle.Length > 0 &&
        !RowTitleText.StartsWith(PanGlossTitle, StringComparison.Ordinal);
    public bool HasYourWords => WordRows.Count > 0;
    public bool HasExactRows => WordRows.Count > 0;
    public bool HasMembershipCandidates => MembershipCandidateRows.Count > 0;
    public bool HasSpellingCandidates => SpellingCandidateRows.Count > 0;
    /// <summary>The number of distinct spelling matches, kept separate from confirmed identity uses.</summary>
    public string SpellingCandidatesText =>
        $"{(HasPartialCount ? "At least " : string.Empty)}{SpellingCandidateRows.Count:N0} " +
        CountWord(SpellingCandidateRows.Count, "spelling match", "spelling matches");
    public bool HasAnyWordRows => HasExactRows || HasMembershipCandidates || HasSpellingCandidates;
    public int? YourWordsCount => AttributionState == WarningDisplayState.ExactUses ? WordRows.Count : null;
    public WarningDisplayState AttributionState { get; }
    public string ReachStateText => AttributionState switch
    {
        WarningDisplayState.ExactUses => IsPartialReach
            ? "Some of your words use an item PanGloss named; Motif could not follow every named connection"
            : "Your words use an item PanGloss named",
        WarningDisplayState.SpellingCandidates => IsPartialReach
            ? "These spellings match; Motif could not follow every named connection"
            : "Matched by spelling only; this does not confirm use of the named item",
        WarningDisplayState.MembershipCandidates => IsPartialReach
            ? "These words use members of the named resource; Motif could not follow every named connection"
            : "These words use members of the named resource; use of the resource is not confirmed",
        WarningDisplayState.NoneInSelection => "None of your words use the item PanGloss named",
        WarningDisplayState.NoFollowedRouteMatch =>
            "No exact word uses were found; Motif could not follow every named connection",
        WarningDisplayState.NoSubject => "PanGloss names nothing here",
        WarningDisplayState.NamedUnsupportedRoute =>
            "PanGloss named an item without a word list",
        WarningDisplayState.ProjectWide => "This item applies across the grammar",
        WarningDisplayState.MissingObject => "The item PanGloss named is missing from this project",
        WarningDisplayState.UnresolvedIdentity when SubjectParts.Any(part =>
            part.Role is GrammarWarningPartRole.Object or GrammarWarningPartRole.Missing) =>
            "Word counts are unavailable for this named item",
        WarningDisplayState.UnresolvedIdentity => "Motif could not match PanGloss's name to a project item",
        WarningDisplayState.EvidenceUnavailable when IsPartialReach =>
            "Word counts are unavailable; Motif could not follow every named connection",
        _ => "Word counts are unavailable for this finding",
    };
    public string LineSummaryText => ReachSummaryText;
    public bool IsPartialReach { get; }
    public bool HasPartialCount => IsPartialReach && AttributionState is
        WarningDisplayState.ExactUses or WarningDisplayState.MembershipCandidates or
        WarningDisplayState.SpellingCandidates;
    public bool CanParseAgain => IsGone && HasYourWords;
    public string Message { get; }
    public string PanGlossTitle { get; }
    public string PanGlossExplanation { get; }
    public string PanGlossGuidance { get; }
    public string PanGlossFieldWorksPlaces { get; }
    public string PanGlossHelp { get; }
    public Uri? PanGlossReference { get; }
    public bool HasExplanation => PanGlossExplanation.Length > 0;
    public bool HasGuidance => PanGlossGuidance.Length > 0;
    public bool HasFieldWorksPlaces => PanGlossFieldWorksPlaces.Length > 0;
    public bool HasHelp => PanGlossHelp.Length > 0;
    public bool HasPanGlossReference => PanGlossReference is not null;
    public bool HasMessage => Message.Length > 0;
    public string YourWordsText => ReachSummaryText;

    private static IReadOnlyList<GrammarWarningDetailViewModel> DetailsFor(IReadOnlyList<GrammarWarning> warnings) =>
        warnings[0].Code == PanGlossFindingCodes.StoredAnalysisIncomplete
            ? [new GrammarWarningDetailViewModel(warnings, WordformsText(warnings))]
            : warnings.GroupBy(DetailIdentity, StringComparer.Ordinal)
                .Select(group => new GrammarWarningDetailViewModel(group.First(), group.Count())).ToArray();

    private static string WordformsText(IReadOnlyList<GrammarWarning> warnings) => string.Join(", ", warnings
        .Select(item => item.Subject.FirstOrDefault(part => part.SourceClass == "WfiWordform")?.Title ??
            item.Subject.FirstOrDefault()?.Text ?? item.Text)
        .GroupBy(word => word, StringComparer.Ordinal)
        .Select(word => word.Count() == 1 ? $"{word.Key} (1 stored analysis)" : $"{word.Key} ({word.Count():N0} stored analyses)"));

    private static string DetailIdentity(GrammarWarning warning)
    {
        var fields = new[]
            {
                warning.Title ?? string.Empty, warning.Description, warning.Explanation ?? string.Empty,
                warning.Guidance ?? string.Empty, warning.HelpUrl ?? string.Empty,
            }
            .Concat(warning.Subject.Select(part => $"{part.Role}:{part.FieldWorksKind}:{part.ObjectId}:{part.Text}:{part.FieldWorksLink}"))
            .Concat(warning.Problem.Select(part => $"{part.Role}:{part.Text}"));
        return string.Concat(fields.Select(field => $"{field.Length}:{field}"));
    }

    private static string SummaryFor(IReadOnlyList<GrammarWarning> namedFindings, WarningWordsTouched? touched)
    {
        if (namedFindings.Count == 0) return "Can't tell: PanGloss names nothing";
        if (touched is not { IsComplete: true }) return string.Empty;
        return touched.Words == 0 ? "None of your words" : $"{touched.Words:N0} of your words";
    }

    private static WarningDisplayState AggregateAttributionState(IReadOnlyList<GrammarWarning> namedFindings,
        WarningWordsTouched? touched)
    {
        if (namedFindings.Count == 0) return WarningDisplayState.NoSubject;
        if (touched is { Words: > 0 }) return WarningDisplayState.ExactUses;
        if (touched is { ByMembershipOnly: > 0 }) return WarningDisplayState.MembershipCandidates;
        if (touched is { BySpellingOnly: > 0 }) return WarningDisplayState.SpellingCandidates;
        if (touched is { IsComplete: true }) return WarningDisplayState.NoneInSelection;
        return WarningAttribution.From(namedFindings[0]);
    }

    private static string CountWord(int count, string singular, string plural) => count == 1 ? singular : plural;

    public string GroupName { get; }
    public string GroupCode { get; }
    public int SourceOrder { get; init; }
    public GrammarDiagnosticLevel Level { get; }
    public bool IsWarning => Level == GrammarDiagnosticLevel.Warning;
    public bool IsError => Level == GrammarDiagnosticLevel.Error;
    public int RepeatCount { get; }
    public string SeenText => $"{RepeatCount:N0}×";
    public string Description => Message;
    public string Severity { get; }
    public Mark SeverityMark => Mark.Of(Level);
    public bool IsInfo => Level == GrammarDiagnosticLevel.Information;
    public string Text { get; }
    public IReadOnlyList<GrammarWarningPart> SubjectParts { get; }
    public IReadOnlyList<GrammarWarningPart> ProblemParts { get; }
    public bool HasProblemParts => ProblemParts.Count > 0 &&
        (ProblemParts.Count != 1 || ProblemParts[0].Role != GrammarWarningPartRole.Text ||
            !string.Equals(ProblemParts[0].Text, Message, StringComparison.Ordinal));
    internal string? FindingIdentity { get; }
    public IRelayCommand ToggleOpenCommand { get; }

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(CanParseAgain))]
    [NotifyPropertyChangedFor(nameof(LineSummaryText))]
    [NotifyPropertyChangedFor(nameof(HasRefreshStatus))]
    private bool _isGone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(LineSummaryText))]
    [NotifyPropertyChangedFor(nameof(HasRefreshStatus))]
    private bool _isNotMatchedAfterRefresh;

    public bool HasRefreshStatus => IsGone || IsNotMatchedAfterRefresh;

    public string StatusText => IsGone
        ? "Gone after Refresh"
        : IsNotMatchedAfterRefresh ? "Earlier line could not be matched after Refresh" : string.Empty;

    public void MarkGone() => IsGone = true;

    public void MarkNotMatched() => IsNotMatchedAfterRefresh = true;
}
