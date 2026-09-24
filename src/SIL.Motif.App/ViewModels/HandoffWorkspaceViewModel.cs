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
/// The Motif window's shell: choosing a project cancels whatever Assessment or AI Handoff run is active, clears
/// the state the previous project displayed, and loads the new project's Baseline, grammar findings, history, and
/// Text state. It owns the sidebar, the project menu, and the freshness line in the top bar, and it publishes each
/// project change and each completed Assessment once, through <see cref="Context"/>, to the page models the page
/// registry builds from that context.
/// </summary>
/// <remarks>
/// What a page does with a published project or Assessment, and how one page opens another, belongs to the pages'
/// own models; the shell names none of them. Nothing here reruns anything on its own: <see cref="RefreshCommand"/>
/// is the only way a new Baseline and a new run start together, and only a person presses it.
/// </remarks>
public sealed partial class HandoffWorkspaceViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>Below this window width the sidebar shows icons alone, with each label as a tooltip.</summary>
    public const double SidebarCollapseWidth = 1100;

    private readonly ICommandClient _commandClient;
    private Task _reloadAfterRefresh = Task.CompletedTask;
    private bool _isRefreshing;
    private bool _refreshCancelled;
    private bool _refreshed;

    public HandoffWorkspaceViewModel(
        ProjectViewModel project, ProjectHistoryViewModel projectHistory, BaselineViewModel baseline,
        GrammarViewModel grammar, SelectionViewModel selection, TextWordsViewModel words,
        AssessViewModel assess, IHandoffFolderPicker folderPicker, IFileDragSource dragSource,
        ICommandClient commandClient)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(projectHistory);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(grammar);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(assess);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(dragSource);
        ArgumentNullException.ThrowIfNull(commandClient);
        _commandClient = commandClient;

        assess.TextWords = words;
        Context = new WorkspaceContext(project, projectHistory, baseline, grammar, selection, words, assess,
            new ChangesViewModel(), commandClient, folderPicker, dragSource);
        OpenConfiguration = () => Context.OpenTexts(TextsTab.Texts);

        Pages = PageRegistry.Entries
            .Select(entry => new PageViewModel(entry.Page, entry.Title, entry.Icon, entry.CreateModel(Context)))
            .ToArray();
        ShowPageCommand = new RelayCommand<WorkspacePage>(Context.OpenPage);

        SelectNewProjectCommand = new AsyncRelayCommand(() => Project.BrowseCommand.ExecuteAsync(null));
        OpenRecentProjectCommand = new AsyncRelayCommand<RecentProjectViewModel>(recent =>
            recent is null ? Task.CompletedTask : SetProjectAsync(recent.FullFwDataPath));
        ConfigureCommand = new RelayCommand(() => OpenConfiguration?.Invoke());

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => HasProject && !_isRefreshing && !Assess.IsActive);
        CancelRefreshCommand = new RelayCommand(CancelRefresh, () => _isRefreshing);
        SeeWhatChangedCommand = new RelayCommand(() => Context.OpenTexts(TextsTab.WhatChanged), () => ShowsSeeWhatChanged);

        Project.ProjectChosen += OnProjectChosen;
        Project.KnownProjects.CollectionChanged += OnKnownProjectsChanged;
        Baseline.Refreshed += OnBaselineRefreshed;
        Baseline.OfferRerun += OnOfferRerun;
        Baseline.PropertyChanged += OnBaselinePropertyChanged;
        Assess.PropertyChanged += OnAssessPropertyChanged;
        Context.PropertyChanged += OnContextPropertyChanged;

        AcceptRerunCommand = new AsyncRelayCommand(AcceptRerunAsync, () => RerunOffered);
        DismissRerunCommand = new RelayCommand(() => RerunOffered = false, () => RerunOffered);
        RefreshPages();
    }

    /// <summary>What every page is built from: the project, its evidence, and the pages' navigation actions.</summary>
    public WorkspaceContext Context { get; }

    /// <summary>The sidebar's entries, in the order <see cref="WorkspacePage"/> declares them.</summary>
    public IReadOnlyList<PageViewModel> Pages { get; }

    /// <summary>The model of type <typeparamref name="TModel"/> the page registry built for this window.</summary>
    public TModel PageModel<TModel>() where TModel : PageModel =>
        Pages.Select(page => page.Model).OfType<TModel>().Single();

    /// <summary>The page the window is showing.</summary>
    public WorkspacePage CurrentPage
    {
        get => Context.CurrentPage;
        set => Context.OpenPage(value);
    }

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

    /// <summary>The chosen project's file name for the top bar, or a prompt before one is chosen.</summary>
    public string ProjectName => Context.ProjectName;

    /// <summary>Whether a project has been chosen in this window.</summary>
    public bool HasProject => Context.HasProject;

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

    // What the numbers on screen were measured against: the published evidence's save, else the Baseline's.
    private DateTimeOffset? NumbersSavedUtc => Context.Evidence?.MeasuredSaveUtc ?? Baseline.SourceLastWriteUtc;

    // The latest save known: the project file as last read, or a Baseline captured from a later save.
    private DateTimeOffset? LatestSaveUtc =>
        Baseline.ProjectLastWriteUtc is { } written && (Baseline.SourceLastWriteUtc is not { } source || written > source)
            ? written
            : Baseline.SourceLastWriteUtc;

    private bool IsSavedSinceTheNumbers => NumbersSavedUtc is { } numbers && LatestSaveUtc is { } latest && latest > numbers;

    private string SavedSinceText()
    {
        var stem = Path.GetFileNameWithoutExtension(Context.ProjectPath);
        return $"{stem} saved {When(LatestSaveUtc!.Value)}; the numbers still describe {When(NumbersSavedUtc!.Value)} " +
            "until you refresh.";
    }

    private string BaselineAndSaveText()
    {
        var stem = Path.GetFileNameWithoutExtension(Context.ProjectPath);
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

    // Opening shows the check stored for this Baseline, and never starts one of its own.
    private async Task LoadStoredGrammarAsync(string path, CancellationToken cancellationToken)
    {
        await Grammar.SetProjectAsync(null, cancellationToken).ConfigureAwait(true);
        var stored = await _commandClient.ReadStoredGrammarCheckAsync(new GrammarCheckRequest(path), cancellationToken)
            .ConfigureAwait(true);
        if (!string.Equals(path, Context.ProjectPath, StringComparison.Ordinal)) return;
        // A stored hit is read, not rerun: pinned by `TheStoredReadStampsTheParserExactlyAsTheCheckDoes`.
        if (stored.Succeeded && stored.Value?.Check is not null)
            await Grammar.SetProjectAsync(path, cancellationToken).ConfigureAwait(true);
    }

    public ProjectViewModel Project => Context.Project;

    public ProjectHistoryViewModel ProjectHistory => Context.ProjectHistory;

    public BaselineViewModel Baseline => Context.Baseline;

    public GrammarViewModel Grammar => Context.Grammar;

    public SelectionViewModel Selection => Context.Selection;

    public TextWordsViewModel Words => Context.Words;

    public AssessViewModel Assess => Context.Assess;


    /// <summary>Whether a successful Baseline capture replaced a Baseline an Assessment already covered.</summary>
    [ObservableProperty]
    private bool _rerunOffered;

    public IAsyncRelayCommand AcceptRerunCommand { get; }

    public IRelayCommand DismissRerunCommand { get; }

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
        Context.ProjectPath = fwDataPath;
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
        await Context.PublishProjectOpenedAsync(fwDataPath, cancellationToken).ConfigureAwait(true);
        RaiseFreshness();
    }

    private async void OnProjectChosen(object? sender, string fwDataPath) =>
        await SetProjectAsync(fwDataPath).ConfigureAwait(true);

    private void OnBaselineRefreshed(object? sender, EventArgs e) => _reloadAfterRefresh = ReloadAfterRefreshAsync();

    // The Text list and grammar findings belong to the Baseline just captured, not the one shown before Refresh.
    private async Task ReloadAfterRefreshAsync()
    {
        if (Context.ProjectPath is not { } path) return;
        var grammar = Grammar.SetProjectAsync(path);
        await Selection.LoadTextsAsync(path).ConfigureAwait(true);
        await ProjectHistory.LoadAsync().ConfigureAwait(true);
        await grammar.ConfigureAwait(true);
        await Context.PublishBaselineCapturedAsync().ConfigureAwait(true);
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
                     !string.Equals(known.FullFwDataPath, Context.ProjectPath, StringComparison.OrdinalIgnoreCase)))
            RecentProjects.Add(new RecentProjectViewModel(known.FullFwDataPath));
        OnPropertyChanged(nameof(RecentProjectsText));
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    private void OnBaselinePropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseFreshness();

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WorkspaceContext.CurrentPage):
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(SelectedPage));
                RefreshPages();
                break;
            case nameof(WorkspaceContext.ProjectPath):
                OnPropertyChanged(nameof(ProjectName));
                OnPropertyChanged(nameof(HasProject));
                break;
            case nameof(WorkspaceContext.Evidence):
                // Freshness describes the evidence on screen, whether a run just produced it or the store held it.
                if (Context.HasEvidence) Baseline.HasAssessment = true;
                RaiseFreshness();
                break;
        }
    }

    private void RefreshPages()
    {
        foreach (var page in Pages) page.IsCurrent = page.Page == CurrentPage;
    }

    private void OnAssessPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssessViewModel.IsActive))
        {
            RefreshCommand.NotifyCanExecuteChanged();
            // A run started from Refresh leaves the person where they are; the top bar says how it is going.
            if (Assess.IsActive && !_isRefreshing) Context.OpenPage(WorkspacePage.Texts);
        }

        if (_isRefreshing) RaiseFreshness();

        if (e.PropertyName == nameof(AssessViewModel.State) && Assess.State == RunState.Completed &&
            Assess.Result is { } result)
        {
            Context.PublishEvidence(new WorkspaceEvidence(result, Assess.CompletedAt, Assess.LastRunWasRerun));
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

        await Context.StopPageWorkAsync().ConfigureAwait(true);
    }

    private void ClearProjectBoundState()
    {
        RerunOffered = false;
        _refreshed = false;

        Assess.Reset();
        Context.ClearProject();
    }

    /// <summary>Cancels and awaits any active run, so nothing keeps running past this workspace's lifetime.</summary>
    public async ValueTask DisposeAsync()
    {
        await Assess.DisposeAsync().ConfigureAwait(true);
        await Context.StopPageWorkAsync().ConfigureAwait(true);
    }
}
