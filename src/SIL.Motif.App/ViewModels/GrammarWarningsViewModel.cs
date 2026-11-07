using System.Collections.ObjectModel;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
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
        warning = WarningDisplayProjection.For(warning);
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
    public string SeenText => RepeatCount > 1
        ? PluralText.Format(MotifAppStrings.WarningsFindingsPattern, RepeatCount) : string.Empty;
    public bool HasSeenText => RepeatCount > 1;
}

/// <summary>A named FieldWorks entry link supplied by PanGloss.</summary>
public sealed record GrammarWarningEntryLink(string Entry, string Tool, Uri Address)
{
    public string Label => string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.WarningsOpenEntryFormat, Entry, Tool);
}

/// <summary>Which grammar-health report levels the Warnings page displays.</summary>
public enum GrammarFindingBucket
{
    All,
    Errors,
    Warnings,
    Information,

    /// <summary>Findings only today's grammar has, shown while the Proposal is paired with today.</summary>
    GoneWithProposal,

    /// <summary>Findings only the open Proposal's grammar has, shown while it is paired with today.</summary>
    NewWithProposal,
}

/// <summary>
/// The mark each grammar finding level wears on its filter chip and row. These marks do not replace PanGloss's
/// outcome marks in a word row.
/// </summary>
public static class GrammarLevelMarks
{
    public const string Error = "❗";
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
    private List<GrammarWarningRowViewModel> _todayRows = [];
    private List<GrammarWarningRowViewModel> _todayResolved = [];
    private GrammarWarningRowViewModel? _selectedWarning;
    private Func<GrammarWarningRowViewModel, Task>? _reparseWords;
    private Action<GrammarWarningRowViewModel>? _handOffWords;
    private WordRowRoutes? _wordRoutes;
    private (string? Identity, bool Open)? _reselect;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownObjectTimingDetailText))]
    private string _objectTimingDetailText = string.Empty;

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

    public bool HasReportedErrors => Reported(GrammarDiagnosticLevel.Error);
    public bool HasReportedWarnings => Reported(GrammarDiagnosticLevel.Warning);
    public bool HasReportedInformation => Reported(GrammarDiagnosticLevel.Information);

    /// <summary>Whether severity distinguishes findings in the current report.</summary>
    public bool HasMultipleSeverities => (HasReportedErrors ? 1 : 0) + (HasReportedWarnings ? 1 : 0) +
        (HasReportedInformation ? 1 : 0) > 1;

    /// <summary>The shared table tracks, omitting the severity column when it cannot distinguish rows.</summary>
    public string TableColumns => HasMultipleSeverities ? "Narrow 2* 2* Narrow Actions" : "0 2* 2* Narrow Actions";

    public int ErrorCount => VisibleFindings()
        .Where(row => row.Level == GrammarDiagnosticLevel.Error).Sum(row => row.RepeatCount);
    public int WarningCount => VisibleFindings()
        .Where(row => row.Level == GrammarDiagnosticLevel.Warning).Sum(row => row.RepeatCount);
    public int InformationCount => VisibleFindings()
        .Where(row => row.Level == GrammarDiagnosticLevel.Information).Sum(row => row.RepeatCount);

    /// <summary>The report's error, warning, and information totals for the page summary.</summary>
    public string BreakdownText => string.Format(System.Globalization.CultureInfo.CurrentCulture,
        MotifAppStrings.WarningsBreakdownFormat,
        PluralText.Format(MotifAppStrings.WarningsErrorsPattern, ErrorCount),
        PluralText.Format(MotifAppStrings.WarningsWarningsPattern, WarningCount),
        PluralText.Format(MotifAppStrings.WarningsInformationPattern, InformationCount));

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

    private bool InBucket(GrammarWarningRowViewModel row) => Bucket switch
    {
        GrammarFindingBucket.Errors => row.Level == GrammarDiagnosticLevel.Error,
        GrammarFindingBucket.Warnings => row.Level == GrammarDiagnosticLevel.Warning,
        GrammarFindingBucket.Information => row.Level == GrammarDiagnosticLevel.Information,
        GrammarFindingBucket.GoneWithProposal => row.ProposalTag == ProposalFindingTag.Gone,
        GrammarFindingBucket.NewWithProposal => row.ProposalTag == ProposalFindingTag.New,
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
        if (_selectedWarning?.FindingIdentity is { } identity) _sortMemory?.SelectionChanged(identity);
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

    public string CountSummary => PairedCount(ShownCount == TotalCount
        ? PluralText.Format(MotifAppStrings.WarningsFindingsPattern, TotalCount)
        : string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.WarningsCountMatchFormat, ShownCount, TotalCount));

    /// <summary>The distinct Selection words reached by exact identity from the findings the rows in view report.</summary>
    public string TouchYourWordsText => WarningWordsQuery.Touched(ShownFindings()) is { } touched
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.WarningsTouchYourWordsFormat, PluralText.Format(MotifAppStrings.WordCountPattern, touched.Words)) : MotifAppStrings.WarningsTouchYourWords;

    private ProjectFilterMemory? _sortMemory;

    internal void UseViewMemory(ProjectViewMemory memory) => _sortMemory = new(memory, "Warnings",
        () => MostYourWordsFirst ? null : ReportOrderName,
        saved => MostYourWordsFirst = !string.Equals(saved, ReportOrderName, StringComparison.Ordinal), isSort: true,
        restoreSelection: _ => { _restoreSelection = true; RestoreSelectedFinding(); });

    // The selected finding is remembered by its finding identity, which survives a refresh and a reopen.
    private bool _restoreSelection;

    private void RestoreSelectedFinding()
    {
        if (!_restoreSelection || _all.Count == 0) return;
        _restoreSelection = false;
        if (_sortMemory?.SavedSelection is not { } identity) return;
        // A finding that is no longer reported selects nothing.
        SelectedTableEntry = TableEntries.OfType<GrammarWarningRowViewModel>()
            .FirstOrDefault(row => row.FindingIdentity == identity && !row.IsGone);
    }

    private const string ReportOrderName = "ReportOrder";

    internal void SetProjectPath(string? path) => _sortMemory?.Open(path);

    /// <summary>The current ordering shown on the sort control.</summary>
    public string SortLabel => MostYourWordsFirst ? MotifAppStrings.WarningsSortMostWords : MotifAppStrings.WarningsSortReportOrder;

    /// <summary>The complete projected findings, including the effective words each finding touches.</summary>
    public IReadOnlyList<GrammarWarning> Findings { get; private set; } = [];

    /// <summary>Replaces today's report, or clears the page when the check has no report.</summary>
    public void Load(IReadOnlyList<GrammarWarning>? warnings, bool preserveResolved = false)
    {
        _today = warnings;
        Findings = warnings ?? [];
        var previous = _todayRows.Concat(_todayResolved).ToArray();
        var next = RowsOf(warnings);
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

        _todayRows = next;
        _todayResolved = resolved;
        BuildProposalRows();
        OnPropertyChanged(nameof(Findings));
        Present();
    }

    private List<GrammarWarningRowViewModel> RowsOf(IReadOnlyList<GrammarWarning>? warnings)
    {
        var rows = new List<GrammarWarningRowViewModel>();
        if (warnings is null) return rows;
        var identified = warnings.Select((warning, index) =>
            (Warning: warning, Index: index, Identity: WarningFindingIdentity.ForGrouping(warning)));
        foreach (var group in identified.GroupBy(item =>
                     (item.Identity, UnidentifiedIndex: item.Identity is null ? item.Index : -1)))
        {
            rows.Add(new GrammarWarningRowViewModel(group.Select(item => item.Warning).ToArray(), _wordRoutes)
            {
                SourceOrder = rows.Count,
            });
        }
        return rows;
    }

    // Draws the rows of the view the bar chose from the row sets already built; a flip builds none.
    private void Present()
    {
        var previous = _selectedWarning;
        var reopen = previous?.IsOpen == true;
        var (live, extra) = RowsInView();
        _all.Clear();
        _all.AddRange(live);
        _all.AddRange(extra);
        ApplySort();
        TotalCount = live.Sum(row => row.RepeatCount);
        _reselect = previous is not null && !_all.Contains(previous) ? (previous.FindingIdentity, reopen) : null;
        OnPropertyChanged(nameof(HasReportedErrors));
        OnPropertyChanged(nameof(HasReportedWarnings));
        OnPropertyChanged(nameof(HasReportedInformation));
        OnPropertyChanged(nameof(HasMultipleSeverities));
        OnPropertyChanged(nameof(TableColumns));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(InformationCount));
        OnPropertyChanged(nameof(BreakdownText));
        OnPropertyChanged(nameof(HasAny));
        OnPropertyChanged(nameof(TouchYourWordsText));
        OnPropertyChanged(nameof(ShownObjectTimingDetailText));
        RaiseProposalCounts();
        Refresh();
    }

    partial void OnTouchYourWordsChanged(bool value) => Refresh();
    partial void OnMostYourWordsFirstChanged(bool value)
    {
        ApplySort();
        Refresh();
        _sortMemory?.Changed();
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
        ShownCount = Countable().Where(Matches).Sum(row => row.RepeatCount);
    }

    private void RefreshTableEntries()
    {
        var groups = Rows.Cast<object>().OfType<GrammarWarningRowViewModel>()
            .GroupBy(WarningArea)
            .OrderBy(group => (int)group.Key)
            .ToArray();
        var wanted = new List<object>();
        foreach (var group in groups)
        {
            var count = group.Where(row => row.ProposalTag != ProposalFindingTag.Gone).Sum(row => row.RepeatCount);
            wanted.Add(new DenseTableGroup(AreaText(group.Key), PluralText.Format(MotifAppStrings.WarningsFindingsPattern, count)));
            wanted.AddRange(group);
        }

        SyncEntries(wanted);
        if (_reselect is { } reselect)
        {
            _reselect = null;
            var match = reselect.Identity is null ? null : TableEntries.OfType<GrammarWarningRowViewModel>()
                .FirstOrDefault(row => row.FindingIdentity == reselect.Identity && !row.IsGone);
            SelectedTableEntry = match;
            if (match is not null) match.IsOpen = reselect.Open;
        }
        else if (SelectedTableEntry is not null && !TableEntries.Contains(SelectedTableEntry))
            SelectedTableEntry = null;
        RestoreSelectedFinding();
    }

    // Edits the shown entries in place, so a row both views share keeps its place, its selection and its scroll.
    private void SyncEntries(List<object> wanted)
    {
        var index = 0;
        foreach (var entry in wanted)
        {
            if (index < TableEntries.Count && SameEntry(TableEntries[index], entry))
            {
                index++;
                continue;
            }

            var found = -1;
            for (var at = index + 1; at < TableEntries.Count && found < 0; at++)
                if (SameEntry(TableEntries[at], entry)) found = at;
            if (found < 0) TableEntries.Insert(index, entry);
            else for (var remove = found - 1; remove >= index; remove--) TableEntries.RemoveAt(remove);
            index++;
        }

        while (TableEntries.Count > index) TableEntries.RemoveAt(TableEntries.Count - 1);
    }

    private static bool SameEntry(object shown, object wanted) =>
        ReferenceEquals(shown, wanted) || shown is DenseTableGroup group && group.Equals(wanted);

    private enum WarningAreaKind { Environments, PhonologicalRules, Affixes, Other }

    private static string AreaText(WarningAreaKind area) => area switch
    {
        WarningAreaKind.Environments => MotifAppStrings.WarningsAreaEnvironments,
        WarningAreaKind.PhonologicalRules => MotifAppStrings.WarningsAreaPhonologicalRules,
        WarningAreaKind.Affixes => MotifAppStrings.WarningsAreaAffixes,
        _ => MotifAppStrings.WarningsAreaOther,
    };

    private static WarningAreaKind WarningArea(GrammarWarningRowViewModel row)
    {
        var kinds = row.Warning.Subject.Select(part => part.FieldWorksKind).Where(kind => kind is not null)
            .Cast<string>().ToArray();
        var code = row.Warning.Code ?? string.Empty;
        if (kinds.Contains("PhEnvironment", StringComparer.Ordinal) ||
            code.Contains("environment", StringComparison.OrdinalIgnoreCase))
            return WarningAreaKind.Environments;
        if (kinds.Any(kind => kind.StartsWith("MoAffix", StringComparison.Ordinal)) ||
            code.Contains("affix", StringComparison.OrdinalIgnoreCase))
            return WarningAreaKind.Affixes;
        if (kinds.Any(kind => kind.StartsWith("Ph", StringComparison.Ordinal)) ||
            code.Contains("phon", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("rule", StringComparison.OrdinalIgnoreCase))
            return WarningAreaKind.PhonologicalRules;
        return WarningAreaKind.Other;
    }

    private bool Matches(object item) =>
        item is GrammarWarningRowViewModel row
        && InBucket(row)
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
        var display = warnings.Select(WarningDisplayProjection.For).ToArray();
        var panGloss = PanGlossWarningText.From(display[0]);
        Findings = warnings;
        Details = DetailsFor(warnings);
        Warning = warning;
        RepeatCount = warnings.Count;
        Level = warning.Severity;
        Severity = Level.ToWireValue();
        SubjectParts = display.SelectMany(item => item.Subject).Distinct().ToArray();
        ProblemParts = display.SelectMany(item => item.Problem).Distinct().ToArray();
        Text = string.Join(Environment.NewLine, display.Select(item => item.Text).Distinct(StringComparer.Ordinal));
        Message = string.Join(Environment.NewLine, Details.Select(detail => detail.Description)
            .Where(description => description.Length > 0).Distinct(StringComparer.Ordinal));
        TableProblemText = display[0].Description.Length > 0 ? display[0].Description : Message;
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
        var namedParts = display.Where(item => item.NamesItem).SelectMany(item => item.Subject)
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
    public string ExactWordsHeading => NamedItemText.Length > 0 ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.WarningsYourWordsThatUseFormat, NamedItemText) : MotifAppStrings.WarningsYourWords;
    /// <summary>Whether the finding names an item with no exact word uses in the Selection.</summary>
    public bool HasNoExactUses => AttributionState == WarningDisplayState.NoneInSelection;
    public string ReachSummaryText { get; }
    /// <summary>Whether a separate reach explanation is needed for this finding.</summary>
    public bool HasReachStateText => !HasNoExactUses || NamedItemText.Length == 0;
    /// <summary>The plain language shown when no Selection word uses the named item.</summary>
    public string NoExactUsesText => NamedItemText.Length > 0
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.WarningsNoneUseFormat, NamedItemText) : MotifAppStrings.WarningsNoneUseNamed;
    /// <summary>The quiet row title, with repeated identical findings counted once.</summary>
    public string RowTitleText => string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.WarningsRowTitleFormat, PanGlossTitle, PluralText.Format(MotifAppStrings.WarningsFindingsPattern, RepeatCount));
    /// <summary>Whether the table lets a person select this row; a heading cannot be selected, a finding can.</summary>
    public bool IsSelectable => true;
    /// <summary>The severity written out for the table's first column: Error, Warning or Note.</summary>
    public string SeverityLabel => Level switch
    {
        GrammarDiagnosticLevel.Error => MotifAppStrings.WarningsSeverityError,
        GrammarDiagnosticLevel.Warning => MotifAppStrings.WarningsSeverityWarning,
        _ => MotifAppStrings.WarningsSeverityNote,
    };
    /// <summary>
    /// The named item for the table's Item column, or the finding's title when it names none, with a count when
    /// identical findings share the row so the row total matches the group's count.
    /// </summary>
    public string TableItemText => RepeatCount > 1
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.WarningsTableItemRepeatedFormat, TableItemName, PluralText.Format(MotifAppStrings.WarningsFindingsPattern, RepeatCount)) : TableItemName;
    /// <summary>The named item, or the finding's title when it names none, without any repeat count.</summary>
    public string TableItemName => NamedItemText.Length > 0 ? NamedItemText : PanGlossTitle;
    /// <summary>
    /// The PanGloss description for the table's problem column. A raw identifier is shown as the name of the
    /// subject or problem part that carries it, or as an unnamed item when no part does.
    /// </summary>
    public string TableProblemText { get; }
    /// <summary>The Named words cell: an exact count, none, or blank when no exact count is available.</summary>
    public string TableNamedWordsText => WarningWordsQuery.Touched(Findings.Where(item => item.NamesItem).ToArray())
        is not { IsComplete: true } ? string.Empty : YourWordsCount is { } count
        ? count == 0 ? MotifAppStrings.WarningsNone : PluralText.Format(MotifAppStrings.WordCountPattern, count)
        : AttributionState == WarningDisplayState.NoneInSelection ? MotifAppStrings.WarningsNone : string.Empty;
    /// <summary>Why the Named words cell is blank, for its tooltip and accessible name; null when it has a count.</summary>
    public string? TableNamedWordsNote => TableNamedWordsText.Length == 0 ? MotifAppStrings.WarningsReachUnavailable : null;
    /// <summary>Whether the separate title says more than the row heading.</summary>
    public bool HasDistinctPanGlossTitle => PanGlossTitle.Length > 0 &&
        !RowTitleText.StartsWith(PanGlossTitle, StringComparison.Ordinal);
    public bool HasYourWords => WordRows.Count > 0;
    public bool HasExactRows => WordRows.Count > 0;
    public bool HasMembershipCandidates => MembershipCandidateRows.Count > 0;
    public bool HasSpellingCandidates => SpellingCandidateRows.Count > 0;
    /// <summary>The number of distinct spelling matches, kept separate from confirmed identity uses.</summary>
    public string SpellingCandidatesText =>
        PluralText.Format(HasPartialCount ? MotifAppStrings.WarningsSpellingMatchesAtLeastPattern
            : MotifAppStrings.WarningsSpellingMatchesPattern, SpellingCandidateRows.Count);
    public bool HasAnyWordRows => HasExactRows || HasMembershipCandidates || HasSpellingCandidates;
    public int? YourWordsCount => AttributionState == WarningDisplayState.ExactUses ? WordRows.Count : null;
    public WarningDisplayState AttributionState { get; }
    public string ReachStateText => AttributionState switch
    {
        WarningDisplayState.ExactUses => IsPartialReach
            ? MotifAppStrings.WarningsReachSomeExact
            : MotifAppStrings.WarningsReachExact,
        WarningDisplayState.SpellingCandidates => IsPartialReach
            ? MotifAppStrings.WarningsReachSpellingPartial
            : MotifAppStrings.WarningsReachSpelling,
        WarningDisplayState.MembershipCandidates => IsPartialReach
            ? MotifAppStrings.WarningsReachMembersPartial
            : MotifAppStrings.WarningsReachMembers,
        WarningDisplayState.NoneInSelection => MotifAppStrings.WarningsReachNone,
        WarningDisplayState.NoFollowedRouteMatch =>
            MotifAppStrings.WarningsReachNoRoute,
        WarningDisplayState.NoSubject => MotifAppStrings.WarningsReachNoSubject,
        WarningDisplayState.NamedUnsupportedRoute =>
            MotifAppStrings.WarningsReachUnsupported,
        WarningDisplayState.ProjectWide => MotifAppStrings.WarningsReachProjectWide,
        WarningDisplayState.MissingObject => MotifAppStrings.WarningsReachMissing,
        WarningDisplayState.UnresolvedIdentity when SubjectParts.Any(part =>
            part.Role is GrammarWarningPartRole.Object or GrammarWarningPartRole.Missing) =>
            MotifAppStrings.WarningsReachUnresolvedNamed,
        WarningDisplayState.UnresolvedIdentity => MotifAppStrings.WarningsReachUnresolved,
        WarningDisplayState.EvidenceUnavailable when IsPartialReach =>
            MotifAppStrings.WarningsReachEvidencePartial,
        _ => MotifAppStrings.WarningsReachUnavailable,
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
        .Select(word => word.Count() == 1
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.WarningsOneStoredAnalysis, word.Key)
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.WarningsStoredAnalysesFormat, word.Key, word.Count().ToString("N0", System.Globalization.CultureInfo.CurrentCulture))));

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
        if (namedFindings.Count == 0) return MotifAppStrings.WarningsCantTell;
        if (touched is not { IsComplete: true }) return string.Empty;
        return touched.Words == 0 ? MotifAppStrings.WarningsNoneOfYourWords
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.WarningsWordsOfYourWordsFormat, touched.Words.ToString("N0", System.Globalization.CultureInfo.CurrentCulture));
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
    public string SeenText => string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.WarningsSeenTimesFormat, RepeatCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture));
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

    /// <summary>What a Request about this Warning names: the finding's identity, or its message when it has none.</summary>
    public string AskTargetId => FindingIdentity ?? Message;
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

    public bool HasRefreshStatus => IsGone || IsNotMatchedAfterRefresh || ProposalTag == ProposalFindingTag.New;

    public string StatusText => ProposalTag switch
    {
        ProposalFindingTag.Gone => MotifAppStrings.WarningsGoneWithProposalStatus,
        ProposalFindingTag.New => MotifAppStrings.WarningsNewWithProposalStatus,
        _ => IsGone ? MotifAppStrings.WarningsGoneAfterRefresh
            : IsNotMatchedAfterRefresh ? MotifAppStrings.WarningsNotMatchedAfterRefresh : string.Empty,
    };

    public void MarkGone() => IsGone = true;

    public void MarkNotMatched() => IsNotMatchedAfterRefresh = true;
}
