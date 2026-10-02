using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

internal static class WarningFindingIdentity
{
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
    private bool _mostYourWordsFirst = true;

    public bool HasAny => _all.Count > 0;

    public string CountSummary => ShownCount == TotalCount
        ? (TotalCount == 1 ? "1 finding" : $"{TotalCount} findings")
        : $"{ShownCount} of {TotalCount} findings match the filters";

    /// <summary>The complete projected findings, including the effective words each finding touches.</summary>
    public IReadOnlyList<GrammarWarning> Findings { get; private set; } = [];

    /// <summary>Replaces the displayed report, or clears the page when the check has no report.</summary>
    public void Load(IReadOnlyList<GrammarWarning>? warnings, bool preserveResolved = false)
    {
        Findings = warnings ?? [];
        OnPropertyChanged(nameof(Findings));
        var previous = _all.ToArray();
        var next = new List<GrammarWarningRowViewModel>();
        if (warnings is not null)
        {
            var identified = warnings.Select((warning, index) =>
                (Warning: warning, Index: index, Identity: WarningFindingIdentity.Of(warning)));
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
            ? "Some of your words use the item PanGloss named; other named connections could not be followed"
            : "Uses the item PanGloss named",
        WarningDisplayState.SpellingCandidates => IsPartialReach
            ? "These spellings match; other named connections could not be followed"
            : "The spelling matches, but that does not confirm the phoneme was used",
        WarningDisplayState.MembershipCandidates => IsPartialReach
            ? "These words use members of the named resource; other named connections could not be followed"
            : "These words use members of the named resource; selection of the resource is not confirmed",
        WarningDisplayState.NoneInSelection => "No words matched the items PanGloss named",
        WarningDisplayState.NoFollowedRouteMatch =>
            "No words matched through the routes Motif could follow; other named connections could not be followed",
        WarningDisplayState.NoSubject => "PanGloss did not name a subject for this finding",
        WarningDisplayState.NamedUnsupportedRoute =>
            "PanGloss named an item, but Motif does not follow this type to words",
        WarningDisplayState.ProjectWide => "This project-wide resource has no word attribution",
        WarningDisplayState.MissingObject => "The item PanGloss named is not in this FieldWorks project",
        WarningDisplayState.UnresolvedIdentity => "The named subject's identity or word reach is unavailable",
        WarningDisplayState.EvidenceUnavailable when IsPartialReach =>
            "Word evidence is unavailable; other named connections could not be followed",
        _ => "Word evidence is not available",
    };
    public string ReachSummaryText => AttributionState switch
    {
        WarningDisplayState.ExactUses => CountText(WordRows.Count, "of your words"),
        WarningDisplayState.MembershipCandidates => CandidateCountText(MembershipCandidateRows.Count,
            "membership candidate"),
        WarningDisplayState.SpellingCandidates => CandidateCountText(SpellingCandidateRows.Count, "spelling match"),
        WarningDisplayState.NoneInSelection => "No words in this Selection",
        WarningDisplayState.NoFollowedRouteMatch => "No matches on followed routes",
        WarningDisplayState.NoSubject => "No subject supplied",
        WarningDisplayState.NamedUnsupportedRoute => "Named item; word route unavailable",
        WarningDisplayState.ProjectWide => "No word attribution for this resource",
        WarningDisplayState.MissingObject => "Named item missing",
        WarningDisplayState.UnresolvedIdentity => "Identity unavailable",
        _ => "Word evidence unavailable",
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
