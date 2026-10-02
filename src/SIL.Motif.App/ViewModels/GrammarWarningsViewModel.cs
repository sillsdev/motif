using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

internal static class WarningFindingIdentity
{
    /// <summary>Finds repeated subjectless rows only when the producer reported identical finding content.</summary>
    public static string? ForGrouping(GrammarWarning warning)
    {
        var identity = Of(warning);
        if (identity is not null) return identity;
        if (warning.Subject.Count != 0 || string.IsNullOrWhiteSpace(warning.Code)) return null;

        var fields = new[]
            {
                warning.Origin.ToString(), warning.Severity.ToString(), warning.Code, warning.Title ?? string.Empty,
                warning.Group ?? string.Empty, warning.Description, warning.Text,
            }
            .Concat(warning.Problem.Select(part => $"{part.Role}:{part.Text}"));
        return string.Concat(fields.Select(field => $"{field.Length}:{field}"));
    }

    public static string? Of(GrammarWarning warning)
    {
        if (string.IsNullOrWhiteSpace(warning.Code)) return null;
        var subjects = warning.Subject
            .Where(part => part.Role is GrammarWarningPartRole.Object or GrammarWarningPartRole.Missing)
            .Select(SubjectIdentityOf)
            .ToArray();
        if (subjects.Length == 0 || subjects.Any(subject => subject is null)) return null;

        var fields = new[] { warning.Origin.ToString(), warning.Code }
            .Concat(subjects.Cast<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        return string.Concat(fields.Select(field => $"{field.Length}:{field}"));
    }

    private static string? SubjectIdentityOf(GrammarWarningPart part)
    {
        if (string.IsNullOrWhiteSpace(part.FieldWorksKind)) return null;
        var ids = new[] { part.SubjectGuid, part.FieldWorksGuid, part.ObjectId }
            .Where(value => Guid.TryParse(value, out _))
            .Select(value => Guid.Parse(value!).ToString("D"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return ids.Length == 1 ? $"{part.FieldWorksKind}:{ids[0]}" : null;
    }
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
    private Func<GrammarWarningRowViewModel, Task>? _reparseWords;
    private Action<GrammarWarningRowViewModel>? _handOffWords;
    private WordRowRoutes? _wordRoutes;

    public GrammarWarningsViewModel()
    {
        Rows = new DataGridCollectionView(_all) { Filter = Matches };
        SetBucketCommand = new RelayCommand<GrammarFindingBucket>(bucket => Bucket = bucket);
        SetReportOrderCommand = new RelayCommand(() => MostYourWordsFirst = false);
        SetMostYourWordsFirstCommand = new RelayCommand(() => MostYourWordsFirst = true);
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
                next.Add(new GrammarWarningRowViewModel(group.First().Warning, group.Count(), _wordRoutes)
                {
                    SourceOrder = next.Count,
                });
            }
        }
        var resolved = new List<GrammarWarningRowViewModel>();
        if (preserveResolved)
        {
            var current = next.Where(row => row.FindingIdentity is not null)
                .Select(row => row.FindingIdentity!).ToHashSet(StringComparer.Ordinal);
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
        ShownCount = VisibleFindings().Where(Matches).Sum(row => row.RepeatCount);
    }

    private bool Matches(object item) =>
        item is GrammarWarningRowViewModel row
        && InBucket(row.Level)
        && (!TouchYourWords || row.AttributionState == WarningDisplayState.ExactUses);

    private IEnumerable<GrammarWarningRowViewModel> VisibleFindings() => _all.Where(row => !row.IsGone);

    private static string CountWord(int count, string singular, string plural) => count == 1 ? singular : plural;

}

/// <summary>One PanGloss finding with its own text, guidance, subjects, and word evidence.</summary>
public sealed partial class GrammarWarningRowViewModel : ObservableObject
{
    public GrammarWarningRowViewModel(GrammarWarning warning, int repeatCount = 1, WordRowRoutes? wordRoutes = null)
    {
        ArgumentNullException.ThrowIfNull(warning);
        var panGloss = PanGlossWarningText.From(warning);
        Warning = warning;
        RepeatCount = repeatCount;
        Level = warning.Severity;
        Severity = Level.ToWireValue();
        SubjectParts = warning.Subject;
        ProblemParts = warning.Problem;
        Text = warning.Text;
        Message = panGloss.Description;
        PanGlossTitle = panGloss.Title;
        PanGlossExplanation = panGloss.Explanation;
        PanGlossGuidance = panGloss.Guidance;
        PanGlossFieldWorksPlaces = panGloss.FieldWorksPlaces;
        PanGlossHelp = panGloss.HelpBody;
        PanGlossReference = panGloss.HelpUrl;
        GroupCode = warning.Code ?? string.Empty;
        GroupName = panGloss.Title;
        FindingIdentity = WarningFindingIdentity.Of(warning);
        YourWords = warning.YourWords;
        NamedItemText = warning.Subject
            .Where(part => part.Role is GrammarWarningPartRole.Object or GrammarWarningPartRole.Missing)
            .Select(part => part.Text).Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal).FirstOrDefault() ?? string.Empty;
        AttributionState = WarningAttribution.From(warning);
        IsPartialReach = WarningAttribution.HasUnfollowedConnections(warning);
        var evidence = warning.YourWords;
        var exactWords = evidence?.Match == WarningWordsMatch.Identity ? evidence.Words : [];
        var membershipWords = (evidence?.Match == WarningWordsMatch.Membership ? evidence.Words : [])
            .Concat(evidence?.MembershipCandidates ?? []);
        var spellingWords = (evidence?.Match == WarningWordsMatch.Spelling ? evidence.Words : [])
            .Concat(evidence?.SpellingCandidates ?? []);
        WordRows = [.. exactWords.Select(word => new WordRowViewModel(word.Row, wordRoutes))];
        MembershipCandidateRows = [.. membershipWords.Select(word => new WordRowViewModel(word.Row, wordRoutes))];
        SpellingCandidateRows = [.. spellingWords.Select(word => new WordRowViewModel(word.Row, wordRoutes))];
        ToggleOpenCommand = new RelayCommand(() => IsOpen = !IsOpen);
    }

    public GrammarWarning Warning { get; }
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
    /// <summary>Whether a separate reach explanation is needed for this finding.</summary>
    public bool HasReachStateText => !HasNoExactUses || NamedItemText.Length == 0;
    /// <summary>The plain language shown when no Selection word uses the named item.</summary>
    public string NoExactUsesText => NamedItemText.Length > 0
        ? $"None of your words use {NamedItemText}" : "None of your words";
    /// <summary>The quiet row title, with repeated identical findings counted once.</summary>
    public string RowTitleText => RepeatCount > 1
        ? $"{PanGlossTitle} · {RepeatCount:N0} findings" : PanGlossTitle;
    public bool HasYourWords => AttributionState == WarningDisplayState.ExactUses && WordRows.Count > 0;
    public bool HasExactRows => WordRows.Count > 0;
    public bool HasMembershipCandidates => MembershipCandidateRows.Count > 0;
    public bool HasSpellingCandidates => SpellingCandidateRows.Count > 0;
    public bool HasAnyWordRows => HasExactRows || HasMembershipCandidates || HasSpellingCandidates;
    public int? YourWordsCount => AttributionState == WarningDisplayState.ExactUses ? WordRows.Count : null;
    public WarningDisplayState AttributionState { get; }
    public string ReachStateText => AttributionState switch
    {
        WarningDisplayState.ExactUses => IsPartialReach
            ? "Some of your words use the item PanGloss named; Motif could not follow every named connection"
            : "Your words use the item PanGloss named",
        WarningDisplayState.SpellingCandidates => IsPartialReach
            ? "These spellings match; Motif could not follow every named connection"
            : "Matched by spelling only; this does not confirm the phoneme was used",
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
    public string ReachSummaryText => AttributionState switch
    {
        WarningDisplayState.ExactUses => CountText(WordRows.Count, "of your words"),
        WarningDisplayState.MembershipCandidates => MembershipCandidateRows.Count == 1
            ? "1 word uses a member of the named resource"
            : $"{MembershipCandidateRows.Count:N0} words use members of the named resource",
        WarningDisplayState.SpellingCandidates => CandidateCountText(SpellingCandidateRows.Count, "spelling match"),
        WarningDisplayState.NoneInSelection => "None of your words",
        WarningDisplayState.NoFollowedRouteMatch => "Word count unavailable",
        WarningDisplayState.NoSubject => "PanGloss names nothing here",
        WarningDisplayState.NamedUnsupportedRoute => "Word count unavailable",
        WarningDisplayState.ProjectWide => "Project-wide item",
        WarningDisplayState.MissingObject => "Item missing from project",
        WarningDisplayState.UnresolvedIdentity => "Word count unavailable",
        _ => "Word count unavailable",
    };
    public string LineSummaryText => HasRefreshStatus ? StatusText : ReachSummaryText;
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

    private string CountText(int count, string noun) =>
        $"{(HasPartialCount ? "At least " : string.Empty)}{count} {noun}";

    private string CandidateCountText(int count, string kind) =>
        $"{(HasPartialCount ? "At least " : string.Empty)}{count} {kind}{(count == 1 ? string.Empty : "s")}";

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
