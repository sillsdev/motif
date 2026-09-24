using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.App.Views;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Composes the child view models of the Motif window into one project workspace: choosing a project cancels
/// whatever Assessment or AI Handoff run is active, clears the state the previous project displayed, and loads
/// the new project's Baseline, grammar findings, history, and Text state. It owns which of the
/// <see cref="WorkspacePage"/>s the window shows and what each sidebar entry counts, the project menu, the
/// freshness line in the top bar, and the one <see cref="ChangesViewModel"/> every page adds to.
/// </summary>
/// <remarks>
/// This is also where the loose ends the child view models leave for composition get wired:
/// <see cref="ViewModels.BaselineViewModel.HasAssessment"/> is set once an Assessment actually completes, and
/// <see cref="ViewModels.StatisticsViewModel.SummaryMarkdown"/> is fed from that same completed Assessment's own
/// rendered summary. Nothing here reruns anything on its own: <see cref="RefreshCommand"/> is the only way a new
/// Baseline and a new run start together, and only a person presses it.
/// </remarks>
public sealed partial class HandoffWorkspaceViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>Below this window width the sidebar shows icons alone, with each label as a tooltip.</summary>
    public const double SidebarCollapseWidth = 1100;

    private readonly ICommandClient _commandClient;
    private string? _projectPath;
    // The FieldWorks save the numbers on screen were measured against, once an Assessment has completed.
    private DateTimeOffset? _assessedSaveUtc;
    private Task _reloadAfterRefresh = Task.CompletedTask;
    private bool _isRefreshing;
    private bool _refreshCancelled;
    private bool _refreshed;

    public HandoffWorkspaceViewModel(
        ProjectViewModel project, ProjectHistoryViewModel projectHistory, BaselineViewModel baseline,
        GrammarViewModel grammar, SelectionViewModel selection, TextWordsViewModel words,
        AssessViewModel assess, StatisticsViewModel statistics, HandoffViewModel handoff, ICommandClient commandClient)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(projectHistory);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(grammar);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(assess);
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentNullException.ThrowIfNull(commandClient);
        _commandClient = commandClient;

        Project = project;
        ProjectHistory = projectHistory;
        Baseline = baseline;
        Grammar = grammar;
        Selection = selection;
        Words = words;
        Assess = assess;
        Assess.TextWords = words;
        Statistics = statistics;
        Handoff = handoff;
        Changes = new ChangesViewModel();
        Assess.Compare.Changes = Changes;
        ResultsInText = new ResultsInTextViewModel(words, assess, ShowWordInWords, OpenTryWord);
        assess.Compare.OpenWord = ShowWordInWords;
        assess.Difference.OpenWord = ShowWordInWords;
        assess.Compare.HandOff = chosen =>
        {
            Handoff.UseWords(chosen);
            CurrentPage = WorkspacePage.AiHandoff;
        };
        assess.OpenTryWord = OpenTryWord;
        Statistics.AssessedWord = Assess.Words.Find;
        Statistics.TryWord = OpenTryWord;
        Statistics.OpenTimeLimit = () => ShowTexts(TextsTab.Texts);
        ResultsInText.OpenTexts = () => ShowTexts(TextsTab.Texts);
        OpenConfiguration = () => ShowTexts(TextsTab.Texts);

        Pages = PageRegistry.Entries.Select(entry => new PageViewModel(entry.Page, entry.Title, entry.Icon)).ToArray();
        ShowPageCommand = new RelayCommand<WorkspacePage>(page => CurrentPage = page);

        SelectNewProjectCommand = new AsyncRelayCommand(() => Project.BrowseCommand.ExecuteAsync(null));
        OpenRecentProjectCommand = new AsyncRelayCommand<RecentProjectViewModel>(recent =>
            recent is null ? Task.CompletedTask : SetProjectAsync(recent.FullFwDataPath));
        ConfigureCommand = new RelayCommand(() => OpenConfiguration?.Invoke());
        CheckGrammarCommand = new AsyncRelayCommand(CheckGrammarAsync, () => HasProject && !Grammar.IsLoading);

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => HasProject && !_isRefreshing && !Assess.IsActive);
        CancelRefreshCommand = new RelayCommand(CancelRefresh, () => _isRefreshing);
        SeeWhatChangedCommand = new RelayCommand(() => ShowTexts(TextsTab.WhatChanged), () => ShowsSeeWhatChanged);

        Project.ProjectChosen += OnProjectChosen;
        Project.KnownProjects.CollectionChanged += OnKnownProjectsChanged;
        Baseline.Refreshed += OnBaselineRefreshed;
        Baseline.OfferRerun += OnOfferRerun;
        Baseline.PropertyChanged += OnChildPropertyChanged;
        Selection.PropertyChanged += OnChildPropertyChanged;
        Words.PropertyChanged += OnChildPropertyChanged;
        Handoff.PropertyChanged += OnChildPropertyChanged;
        Assess.PropertyChanged += OnAssessPropertyChanged;
        Grammar.PropertyChanged += OnChildPropertyChanged;
        Grammar.Warnings.PropertyChanged += OnChildPropertyChanged;
        Changes.Items.CollectionChanged += OnChangesChanged;

        AcceptRerunCommand = new AsyncRelayCommand(AcceptRerunAsync, () => RerunOffered);
        DismissRerunCommand = new RelayCommand(() => RerunOffered = false, () => RerunOffered);
        RefreshPages();
    }

    /// <summary>The sidebar's entries, in the order <see cref="WorkspacePage"/> declares them.</summary>
    public IReadOnlyList<PageViewModel> Pages { get; }

    /// <summary>The page the window is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPage))]
    private WorkspacePage _currentPage;

    /// <summary>The sidebar entry for <see cref="CurrentPage"/>; setting it opens that page.</summary>
    public PageViewModel SelectedPage
    {
        get => PageOf(CurrentPage);
        set
        {
            if (value is not null) CurrentPage = value.Page;
        }
    }

    /// <summary>Opens the page passed as the command parameter.</summary>
    public IRelayCommand<WorkspacePage> ShowPageCommand { get; }

    /// <summary>The Texts page's own state, such as which tab it shows.</summary>
    public TextsPageViewModel TextsPage { get; } = new();

    /// <summary>Opens the Texts page on <paramref name="tab"/>.</summary>
    public void ShowTexts(TextsTab tab)
    {
        TextsPage.Tab = tab;
        CurrentPage = WorkspacePage.Texts;
    }

    /// <summary>Whether the window is too narrow for labels, so the sidebar shows icons alone.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidebarExpanded))]
    private bool _isSidebarCollapsed;

    /// <summary>The negation of <see cref="IsSidebarCollapsed"/>, so a view never composes <c>!</c> itself.</summary>
    public bool IsSidebarExpanded => !IsSidebarCollapsed;

    /// <summary>The sidebar entry for <paramref name="page"/>.</summary>
    public PageViewModel PageOf(WorkspacePage page) => Pages.First(entry => entry.Page == page);

    /// <summary>Collapses or expands the sidebar for a window <paramref name="width"/> pixels wide.</summary>
    public void UpdateWindowWidth(double width) => IsSidebarCollapsed = width < SidebarCollapseWidth;

    /// <summary>The changes collected on any page, to become one Proposal; the Review page lists them.</summary>
    public ChangesViewModel Changes { get; }

    /// <summary>The chosen project's file name for the top bar, or a prompt before one is chosen.</summary>
    public string ProjectName => _projectPath is null ? "Choose a project" : Path.GetFileName(_projectPath);

    /// <summary>Whether a project has been chosen in this window.</summary>
    public bool HasProject => _projectPath is not null;

    /// <summary>Browses for a <c>.fwdata</c> file and opens it: the project menu's Select new.</summary>
    public IAsyncRelayCommand SelectNewProjectCommand { get; }

    /// <summary>The machine's Known projects other than the open one, most recently seen first.</summary>
    public ObservableCollection<RecentProjectViewModel> RecentProjects { get; } = [];

    /// <summary>The recent projects' names in one line, for the project menu's Open recent entry.</summary>
    public string RecentProjectsText => RecentProjects.Count == 0
        ? "No other projects on this computer yet"
        : string.Join(" · ", RecentProjects.Take(3).Select(project => project.Name));

    /// <summary>Whether Open recent has anything to open.</summary>
    public bool HasRecentProjects => RecentProjects.Count > 0;

    /// <summary>Opens the recent project passed as the command parameter.</summary>
    public IAsyncRelayCommand<RecentProjectViewModel> OpenRecentProjectCommand { get; }

    /// <summary>Runs <see cref="OpenConfiguration"/>: the project menu's Configure.</summary>
    public IRelayCommand ConfigureCommand { get; }

    /// <summary>
    /// What Configure opens. By default the Texts page's Texts tab, where the texts, added words and time limits
    /// an Assessment uses are chosen; a setup dialog replaces it by setting this.
    /// </summary>
    public Action? OpenConfiguration { get; set; }

    /// <summary>Whether the numbers on screen describe the project as FieldWorks last saved it.</summary>
    public ProjectFreshness Freshness =>
        !HasProject ? ProjectFreshness.NoProject
        : _isRefreshing ? ProjectFreshness.Refreshing
        : IsSavedSinceTheNumbers ? ProjectFreshness.SavedSince
        : !Baseline.HasBaseline ? ProjectFreshness.NoBaseline
        : _refreshed ? ProjectFreshness.Refreshed
        : ProjectFreshness.Current;

    /// <summary>Whether the top bar has a freshness line to show.</summary>
    public bool HasFreshness => Freshness != ProjectFreshness.NoProject;

    /// <summary>The freshness line's state in words.</summary>
    public string FreshnessLabel => Freshness switch
    {
        ProjectFreshness.NoBaseline => "No Baseline yet",
        ProjectFreshness.Current => "Current",
        ProjectFreshness.SavedSince => "FieldWorks saved since",
        ProjectFreshness.Refreshing => "Refreshing",
        ProjectFreshness.Refreshed => "Refreshed",
        _ => string.Empty,
    };

    /// <summary>The freshness line's detail: which save the numbers describe, or what the Refresh is doing.</summary>
    public string FreshnessDetail => Freshness switch
    {
        ProjectFreshness.NoBaseline => "Refresh to capture one from FieldWorks' last save.",
        ProjectFreshness.Current => BaselineAndSaveText(),
        ProjectFreshness.SavedSince => SavedSinceText(),
        ProjectFreshness.Refreshing => Assess.IsActive
            ? Assess.Progress?.Message is { Length: > 0 } message ? message : "Assessing the Selection..."
            : "Capturing a new Baseline...",
        ProjectFreshness.Refreshed => Assess.Difference.HasDifference
            ? Assess.Difference.Summary
            : "A new Baseline, and the Selection assessed against it.",
        _ => string.Empty,
    };

    /// <summary>Whether the freshness line reads as up to date.</summary>
    public bool FreshnessIsCurrent => Freshness is ProjectFreshness.Current or ProjectFreshness.Refreshed;

    /// <summary>Whether the freshness line warns that the numbers describe an older save.</summary>
    public bool FreshnessIsStale => Freshness is ProjectFreshness.SavedSince or ProjectFreshness.NoBaseline;

    /// <summary>Whether a Refresh is under way.</summary>
    public bool FreshnessIsBusy => Freshness == ProjectFreshness.Refreshing;

    /// <summary>Whether a finished Refresh moved some words, so there is something to see.</summary>
    public bool ShowsSeeWhatChanged => Freshness == ProjectFreshness.Refreshed && Assess.Difference.HasDifference;

    /// <summary>Captures a new Baseline, then assesses the Selection against it when there is one to assess.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Stops a Refresh: the capture finishes, but no Assessment follows it.</summary>
    public IRelayCommand CancelRefreshCommand { get; }

    /// <summary>Opens what a finished Refresh changed.</summary>
    public IRelayCommand SeeWhatChangedCommand { get; }

    /// <summary>
    /// Reads the recorded Baseline and the project file's last-write time again, so a save FieldWorks made while
    /// the window was elsewhere shows at once. Reads only; nothing reruns.
    /// </summary>
    public async Task CheckFreshnessAsync(CancellationToken cancellationToken = default)
    {
        if (!HasProject || _isRefreshing) return;
        await Baseline.CheckAsync(cancellationToken).ConfigureAwait(true);
        RaiseFreshness();
    }

    // What the numbers on screen were measured against: the last completed Assessment's save, else the Baseline's.
    private DateTimeOffset? NumbersSavedUtc => _assessedSaveUtc ?? Baseline.SourceLastWriteUtc;

    // The latest save known: the project file as last read, or a Baseline captured from a later save.
    private DateTimeOffset? LatestSaveUtc =>
        Baseline.ProjectLastWriteUtc is { } written && (Baseline.SourceLastWriteUtc is not { } source || written > source)
            ? written
            : Baseline.SourceLastWriteUtc;

    private bool IsSavedSinceTheNumbers => NumbersSavedUtc is { } numbers && LatestSaveUtc is { } latest && latest > numbers;

    private string SavedSinceText()
    {
        var stem = Path.GetFileNameWithoutExtension(_projectPath);
        return $"{stem} saved {When(LatestSaveUtc!.Value)}; the numbers still describe {When(NumbersSavedUtc!.Value)} " +
            "until you refresh.";
    }

    private string BaselineAndSaveText()
    {
        var stem = Path.GetFileNameWithoutExtension(_projectPath);
        var baseline = Baseline.CapturedUtc is { } captured ? $"Baseline of {When(captured)}" : "Baseline";
        var written = Baseline.ProjectLastWriteUtc ?? Baseline.SourceLastWriteUtc;
        return written is { } at ? $"{baseline} · {stem} saved {When(at)}" : baseline;
    }

    private static string When(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        return local.Date == DateTime.Today
            ? local.ToString("t", CultureInfo.CurrentCulture) + " today"
            : local.ToString("ddd d MMM, ", CultureInfo.CurrentCulture) + local.ToString("t", CultureInfo.CurrentCulture);
    }

    /// <summary>Whether the open project's grammar has no check to show, so the Warnings page offers one.</summary>
    public bool IsGrammarNotChecked => HasProject && !Grammar.HasChecked && !Grammar.IsLoading;

    /// <summary>Checks the open project's grammar: started only by a person, since it can take a minute.</summary>
    public IAsyncRelayCommand CheckGrammarCommand { get; }

    private Task CheckGrammarAsync() => _projectPath is { } path ? Grammar.SetProjectAsync(path) : Task.CompletedTask;

    // Opening shows the check stored for this Baseline, and never starts one of its own.
    private async Task LoadStoredGrammarAsync(string path, CancellationToken cancellationToken)
    {
        await Grammar.SetProjectAsync(null, cancellationToken).ConfigureAwait(true);
        var stored = await _commandClient.ReadStoredGrammarCheckAsync(new GrammarCheckRequest(path), cancellationToken)
            .ConfigureAwait(true);
        if (!string.Equals(path, _projectPath, StringComparison.Ordinal)) return;
        // A stored hit is read, not rerun: pinned by `TheStoredReadStampsTheParserExactlyAsTheCheckDoes`.
        if (stored.Succeeded && stored.Value?.Check is not null)
            await Grammar.SetProjectAsync(path, cancellationToken).ConfigureAwait(true);
        RaiseGrammarState();
    }

    private void RaiseGrammarState()
    {
        OnPropertyChanged(nameof(IsGrammarNotChecked));
        CheckGrammarCommand.NotifyCanExecuteChanged();
    }

    /// <summary>What the AI Handoff page's action reads: the first write, or a rewrite.</summary>
    public string HandoffActionText => Handoff.HasCompletedFiles ? "Write the AI Handoff again" : "Write the AI Handoff";

    partial void OnCurrentPageChanged(WorkspacePage value) => RefreshPages();

    public ProjectViewModel Project { get; }

    public ProjectHistoryViewModel ProjectHistory { get; }

    public BaselineViewModel Baseline { get; }

    public GrammarViewModel Grammar { get; }

    public SelectionViewModel Selection { get; }

    public TextWordsViewModel Words { get; }

    public AssessViewModel Assess { get; }

    public StatisticsViewModel Statistics { get; }

    public HandoffViewModel Handoff { get; }

    /// <summary>The Texts page's In text view: the chosen Texts, each occurrence against the Assessment.</summary>
    public ResultsInTextViewModel ResultsInText { get; }

    // What an AI Handoff written now would cover, so the reader knows which run the chat model will see.
    private static string CoverageOf(DateTimeOffset? at, string words, int texts, int pasted)
    {
        var sources = new List<string>();
        if (texts > 0) sources.Add(texts == 1 ? "1 text" : $"{texts} texts");
        if (pasted > 0) sources.Add(pasted == 1 ? "1 pasted word" : $"{pasted} pasted words");
        var from = sources.Count > 0 ? " from " + string.Join(" and ", sources) : string.Empty;
        return at is { } when
            ? $"Covers the Assessment of {when.ToLocalTime():ddd d MMM, h:mm tt}: {words}{from}."
            : $"Covers the latest Assessment: {words}{from}.";
    }

    // Opens a word in the Words tab with every filter cleared, so the word is certain to be listed.
    private void ShowWordInWords(string word)
    {
        SelectWord(word);
        ShowTexts(TextsTab.Words);
    }

    private void SelectWord(string word)
    {
        Assess.Words.WordFilter = string.Empty;
        Assess.Words.SelectedFilter = ResultsWordFilter.All;
        if (Assess.Words.Rows.All(row => row.Word != word)) Assess.Compare.ClearSelectionCommand.Execute(null);
        Assess.Words.SelectedRow = Assess.Words.Rows.FirstOrDefault(row => row.Word == word) ?? Assess.Words.SelectedRow;
    }

    // Asked for by a click, so this traces straight away rather than only priming the box.
    private void OpenTryWord(string word)
    {
        SelectWord(word);
        Assess.Trace.SetWord(word);
        CurrentPage = WorkspacePage.TryAWord;
        if (Assess.Trace.TryCommand.CanExecute(null)) _ = Assess.Trace.TryCommand.ExecuteAsync(null);
    }

    /// <summary>Whether a successful Baseline capture replaced a Baseline an Assessment already covered.</summary>
    [ObservableProperty]
    private bool _rerunOffered;

    /// <summary>Whether the current project has completed at least one Assessment since it was chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotYetAssessed))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyResults))]
    private bool _hasEverAssessed;

    /// <summary>The negation <see cref="HasEverAssessed"/> binds against, so a view never composes <c>!</c> itself.</summary>
    public bool NotYetAssessed => !HasEverAssessed;

    /// <summary>Whether the word views have nothing to show and nothing to explain yet: no run, no refusal.</summary>
    public bool ShowEmptyResults => NotYetAssessed && !Assess.IsActive && Assess.Refusal is null;

    public IAsyncRelayCommand AcceptRerunCommand { get; }

    public IRelayCommand DismissRerunCommand { get; }

    /// <summary>Whether Project and Selection controls should accept input right now.</summary>
    public bool ProjectAndSelectionEnabled => !Assess.IsActive;

    partial void OnRerunOfferedChanged(bool value)
    {
        AcceptRerunCommand.NotifyCanExecuteChanged();
        DismissRerunCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Cancels any active Assessment or AI Handoff run, clears whatever the previous project displayed, and
    /// loads the newly chosen project's Baseline and Text state.
    /// </summary>
    public async Task SetProjectAsync(string fwDataPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);

        await CancelActiveWorkAsync().ConfigureAwait(true);
        ClearProjectBoundState();
        _projectPath = fwDataPath;
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(HasProject));
        RaiseGrammarState();
        Project.ShowChosen(fwDataPath);
        CurrentPage = WorkspacePage.Overview;
        RefreshRecentProjects();
        RaiseFreshness();

        var grammar = LoadStoredGrammarAsync(fwDataPath, cancellationToken);
        await ProjectHistory.SetProjectAsync(fwDataPath, cancellationToken).ConfigureAwait(true);
        await Baseline.SetProjectAsync(fwDataPath, cancellationToken).ConfigureAwait(true);
        await Selection.SetProjectAsync(fwDataPath, cancellationToken).ConfigureAwait(true);
        await Words.SetProjectAsync(fwDataPath, cancellationToken).ConfigureAwait(true);
        await grammar.ConfigureAwait(true);

        Assess.ProjectPath = fwDataPath;
        Statistics.ProjectPath = fwDataPath;
        Handoff.ProjectPath = fwDataPath;
        RaiseFreshness();
    }

    private async void OnProjectChosen(object? sender, string fwDataPath) =>
        await SetProjectAsync(fwDataPath).ConfigureAwait(true);

    private void OnBaselineRefreshed(object? sender, EventArgs e) => _reloadAfterRefresh = ReloadAfterRefreshAsync();

    // The Text list and grammar findings belong to the Baseline just captured, not the one shown before Refresh.
    private async Task ReloadAfterRefreshAsync()
    {
        if (_projectPath is not { } path) return;
        var grammar = Grammar.SetProjectAsync(path);
        await Selection.LoadTextsAsync(path).ConfigureAwait(true);
        await ProjectHistory.LoadAsync().ConfigureAwait(true);
        await grammar.ConfigureAwait(true);
    }

    private void OnOfferRerun(object? sender, EventArgs e) => RerunOffered = true;

    private async Task RefreshAsync()
    {
        _isRefreshing = true;
        _refreshCancelled = false;
        _refreshed = false;
        RaiseFreshness();
        try
        {
            await Baseline.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            if (Baseline.RefusalMessage is not null) return;
            await _reloadAfterRefresh.ConfigureAwait(true);
            if (_refreshCancelled || !Assess.RunCommand.CanExecute(null)) return;

            // The run this Refresh starts is the rerun a fresh Baseline would otherwise offer.
            RerunOffered = false;
            await Assess.RunCommand.ExecuteAsync(null).ConfigureAwait(true);
            _refreshed = Assess.State == RunState.Completed;
        }
        finally
        {
            _isRefreshing = false;
            RaiseFreshness();
        }
    }

    private void CancelRefresh()
    {
        _refreshCancelled = true;
        if (Assess.IsActive) Assess.CancelCommand.Execute(null);
    }

    private void RaiseFreshness()
    {
        OnPropertyChanged(nameof(Freshness));
        OnPropertyChanged(nameof(HasFreshness));
        OnPropertyChanged(nameof(FreshnessLabel));
        OnPropertyChanged(nameof(FreshnessDetail));
        OnPropertyChanged(nameof(FreshnessIsCurrent));
        OnPropertyChanged(nameof(FreshnessIsStale));
        OnPropertyChanged(nameof(FreshnessIsBusy));
        OnPropertyChanged(nameof(ShowsSeeWhatChanged));
        RefreshCommand.NotifyCanExecuteChanged();
        CancelRefreshCommand.NotifyCanExecuteChanged();
        SeeWhatChangedCommand.NotifyCanExecuteChanged();
    }

    private void OnKnownProjectsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshRecentProjects();

    private void RefreshRecentProjects()
    {
        RecentProjects.Clear();
        foreach (var known in Project.KnownProjects.Where(known =>
                     !string.Equals(known.FullFwDataPath, _projectPath, StringComparison.OrdinalIgnoreCase)))
            RecentProjects.Add(new RecentProjectViewModel(known.FullFwDataPath));
        OnPropertyChanged(nameof(RecentProjectsText));
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    private void OnChangesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshPages();

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HandoffActionText));
        if (ReferenceEquals(sender, Baseline)) RaiseFreshness();
        if (ReferenceEquals(sender, Grammar)) RaiseGrammarState();
        RefreshPages();
    }

    private void RefreshPages()
    {
        PageOf(WorkspacePage.Warnings).Badge = Grammar.ShowFindings
            ? Grammar.Warnings.TotalCount.ToString(CultureInfo.CurrentCulture)
            : string.Empty;
        PageOf(WorkspacePage.Review).Badge = Changes.Items.Count > 0
            ? Changes.Items.Count.ToString(CultureInfo.CurrentCulture)
            : string.Empty;

        foreach (var page in Pages) page.IsCurrent = page.Page == CurrentPage;
    }

    private void OnAssessPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ShowEmptyResults));

        if (e.PropertyName == nameof(AssessViewModel.IsActive))
        {
            OnPropertyChanged(nameof(ProjectAndSelectionEnabled));
            RefreshCommand.NotifyCanExecuteChanged();
            // A run started from Refresh leaves the person where they are; the top bar says how it is going.
            if (Assess.IsActive && !_isRefreshing) ShowTexts(TextsTab.Matrix);
            else if (Assess.IsActive) TextsPage.Tab = TextsTab.Matrix;
        }

        if (_isRefreshing) RaiseFreshness();
        RefreshPages();

        if (e.PropertyName == nameof(AssessViewModel.State) && Assess.State == RunState.Completed)
        {
            Baseline.HasAssessment = true;
            _assessedSaveUtc = Assess.Result?.Baseline.SourceLastWriteUtc;
            RaiseFreshness();
            Statistics.Reset();
            Statistics.SummaryMarkdown = Assess.Result?.SummaryMarkdown;
            Statistics.AssessmentId = Assess.Result?.Measurements
                .SingleOrDefault(measurement => measurement.Kind == "ObjectTiming")?.AssessmentId;
            Handoff.InvocationId = Assess.Result?.InvocationId;
            Handoff.LatestAssessmentAt = Assess.CompletedAt;
            Handoff.CoverageText = CoverageOf(Assess.CompletedAt, Assess.Words.CountSummary,
                Selection.ChosenTextIds.Count, Selection.PastedWordEntries.Count);
            HasEverAssessed = true;
            // A re-run exists to settle words, so what it settled is the first thing to see.
            if (Assess.LastRunWasRerun && Assess.Difference.HasDifference) TextsPage.Tab = TextsTab.WhatChanged;
            // The Overview's history lists this Assessment as soon as it is stored.
            _ = ProjectHistory.LoadAsync();
            Words.ShowAssessment(Assess.Words.Find);
        }
    }

    private async Task AcceptRerunAsync()
    {
        RerunOffered = false;
        if (Assess.RunCommand.CanExecute(null)) await Assess.RunCommand.ExecuteAsync(null);
    }

    // Awaits each command's own unwind rather than disposing it: the workspace outlives one project.
    private async Task CancelActiveWorkAsync()
    {
        if (Assess.IsActive)
        {
            Assess.CancelCommand.Execute(null);
            if (Assess.RunCommand.ExecutionTask is { } running) await running.ConfigureAwait(true);
        }

        if (Handoff.IsActive)
        {
            Handoff.CancelCommand.Execute(null);
            if (Handoff.RunCommand.ExecutionTask is { } running) await running.ConfigureAwait(true);
        }
    }

    private void ClearProjectBoundState()
    {
        RerunOffered = false;
        HasEverAssessed = false;
        _refreshed = false;
        _assessedSaveUtc = null;

        Assess.Reset();
        Assess.Trace.Reset();
        Words.ShowAssessment(null);

        Statistics.Reset();

        Handoff.Reset();
        Changes.ClearCommand.Execute(null);
    }

    /// <summary>Cancels and awaits any active run, so nothing keeps running past this workspace's lifetime.</summary>
    public async ValueTask DisposeAsync()
    {
        await Assess.DisposeAsync().ConfigureAwait(true);
        await Handoff.DisposeAsync().ConfigureAwait(true);
    }
}
