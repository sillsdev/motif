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
/// the state the previous project displayed, and loads the new project's Baseline and Selection. It owns the
/// sidebar, project menu, and freshness line, and publishes project and Assessment changes through
/// <see cref="Context"/> to the page models.
/// </summary>
/// <remarks>
/// Page models load their own project queries and handle requests through the context. The shell starts a new
/// Baseline and Assessment together only through <see cref="RefreshCommand"/>.
/// </remarks>
public sealed partial class HandoffWorkspaceViewModel : ObservableObject, IAsyncDisposable
{
    private const string OpenProjectRefusalText = "Motif could not open this project.";

    /// <summary>Below this window width the sidebar shows icons alone, with each label as a tooltip.</summary>
    public const double SidebarCollapseWidth = 1100;

    private Task _reloadAfterRefresh = Task.CompletedTask;
    private bool _isRefreshing;
    private bool _refreshCancelled;
    private bool _refreshed;
    private int _refreshGeneration;

    public HandoffWorkspaceViewModel(
        ProjectViewModel project, BaselineViewModel baseline, SelectionViewModel selection, AssessViewModel assess, IHandoffFolderPicker folderPicker, IFileDragSource dragSource,
        ICommandClient commandClient, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(assess);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(dragSource);
        ArgumentNullException.ThrowIfNull(commandClient);

        Project = project;
        Baseline = baseline;
        Context = new WorkspaceContext(selection, assess, new ChangesViewModel(commandClient), commandClient, folderPicker,
            dragSource, baseline, clock)
        {
            KnownProjects = project.KnownProjects,
            BrowseForProjectCommand = project.BrowseCommand,
            OpenProjectCommand = new AsyncRelayCommand<string>(path =>
                path is null ? Task.CompletedTask : OpenProjectSafelyAsync(path)),
            RefreshBaselineCommand = baseline.RefreshCommand,
        };
        PublishBaseline();
        Pages = PageRegistry.Entries
            .Select(entry => new PageViewModel(entry.Page, entry.Title, entry.Icon, entry.CreateModel(Context)))
            .ToArray();
        var setup = new SetupViewModel(Context, PageModel<TextsPageModel>().Words);
        Context.AttachSetup(setup);
        OpenConfiguration = setup.OpenForConfiguration;
        ShowPageCommand = new RelayCommand<WorkspacePage>(Context.OpenPage);

        SelectNewProjectCommand = new AsyncRelayCommand(() => Project.BrowseCommand.ExecuteAsync(null));
        OpenRecentProjectCommand = new AsyncRelayCommand<RecentProjectViewModel>(recent =>
            recent is null ? Task.CompletedTask : OpenProjectSafelyAsync(recent.FullFwDataPath));
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
        Context.Evidence.PropertyChanged += OnEvidencePropertyChanged;

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

    /// <summary>Whether the project menu can switch projects while shell work is active.</summary>
    public bool ProjectSwitchEnabled => Context.ProjectAndSelectionEnabled && !_isRefreshing;

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

    /// <summary>What the project menu's Configure entry opens.</summary>
    public Action? OpenConfiguration { get; set; }

    /// <summary>Whether the numbers on screen describe the project as FieldWorks last saved it.</summary>
    public ProjectFreshness Freshness =>
        !HasProject ? ProjectFreshness.NoProject
        : _isRefreshing ? ProjectFreshness.Refreshing
        : Context.Evidence.IsStale ? ProjectFreshness.SavedSince
        : Context.Evidence.Freshness == NumbersFreshness.NoBaseline ? ProjectFreshness.NoBaseline
        : _refreshed ? ProjectFreshness.Refreshed
        : ProjectFreshness.Current;

    /// <summary>Whether the top bar has a freshness line to show.</summary>
    public bool HasFreshness => Freshness != ProjectFreshness.NoProject;

    /// <summary>Whether the last Baseline capture was refused.</summary>
    public bool HasRefreshRefusal => Baseline.ShownRefusal is not null;

    /// <summary>The fixed sentence shown when Motif cannot open a chosen project.</summary>
    public string? OpenRefusalMessage { get; private set; }

    /// <summary>The copyable error details from the refused project open.</summary>
    public string? OpenRefusalDetail { get; private set; }

    /// <summary>Whether the last attempt to open a project was refused.</summary>
    public bool HasOpenRefusal => OpenRefusalMessage is not null;

    /// <summary>The freshness line's state in words.</summary>
    public string FreshnessLabel => HasRefreshRefusal ? "Refresh refused" : Freshness switch
    {
        ProjectFreshness.NoBaseline => "No Baseline yet",
        ProjectFreshness.Current => "Current",
        ProjectFreshness.SavedSince => Context.Evidence.AppliedSinceRefresh ? "Numbers need refresh" : "FieldWorks saved since",
        ProjectFreshness.Refreshing => "Refreshing",
        ProjectFreshness.Refreshed => "Refreshed",
        _ => string.Empty,
    };

    /// <summary>The freshness line's detail: which save the numbers describe, or what the Refresh is doing.</summary>
    public string FreshnessDetail
    {
        get
        {
            if (HasRefreshRefusal) return string.Empty;

            var detail = Freshness switch
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
            if (Baseline.FieldWorksHeldProject != true) return detail;
            return string.IsNullOrEmpty(detail) ? Baseline.HeldStatusText : $"{detail} · {Baseline.HeldStatusText}";
        }
    }

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
    /// Reads the recorded Baseline, the project file's last-write time, the pending changes and the stored
    /// evidence again, so a save FieldWorks made, or an Assessment recorded, while the window was elsewhere shows at
    /// once. Reads only; nothing reruns.
    /// </summary>
    public async Task CheckFreshnessAsync(CancellationToken cancellationToken = default)
    {
        if (!HasProject || _isRefreshing) return;
        await Baseline.CheckAsync(cancellationToken).ConfigureAwait(true);
        await Context.Changes.ReloadAsync(cancellationToken).ConfigureAwait(true);
        // A run under way publishes its own result, which a stored read must not replace.
        if (!Assess.IsActive) await Context.ReadStoredEvidenceAsync(cancellationToken).ConfigureAwait(true);
        RaiseFreshness();
    }

    private string SavedSinceText()
    {
        var evidence = Context.Evidence;
        if (evidence.AppliedSinceRefresh)
            return "Changes were applied to the FieldWorks project. The numbers are stale until you refresh.";
        var stem = Path.GetFileNameWithoutExtension(Context.ProjectPath);
        return $"{stem} saved {When(evidence.LatestSaveUtc!.Value)}; the numbers still describe " +
            $"{When(evidence.MeasuredSaveUtc!.Value)} until you refresh.";
    }

    private string BaselineAndSaveText()
    {
        var stem = Path.GetFileNameWithoutExtension(Context.ProjectPath);
        var baseline = Baseline.CapturedUtc is { } captured ? $"Baseline of {When(captured)}" : "Baseline";
        var written = Baseline.ProjectLastWriteUtc ?? Baseline.SourceLastWriteUtc;
        return written is { } at ? $"{baseline} · {stem} saved {When(at)}" : baseline;
    }

    private string When(DateTimeOffset at)
    {
        var clock = Context.Clock;
        var local = TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone);
        return local.Date == clock.GetLocalNow().Date
            ? local.ToString("t", CultureInfo.CurrentCulture) + " today"
            : local.ToString("ddd d MMM, ", CultureInfo.CurrentCulture) + local.ToString("t", CultureInfo.CurrentCulture);
    }

    /// <summary>The project picker behind the project menu.</summary>
    public ProjectViewModel Project { get; }

    /// <summary>The open project's Baseline, which the freshness line and Refresh read.</summary>
    public BaselineViewModel Baseline { get; }

    public SelectionViewModel Selection => Context.Selection;

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
    internal async Task SetProjectAsync(string fwDataPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);
        InvalidateRefreshForProjectSwitch();
        RerunOffered = false;
        _refreshed = false;
        Project.ShowChosen(fwDataPath);
        var opening = Context.OpenProjectAsync(fwDataPath, cancellationToken);
        RaiseFreshness();
        try
        {
            await opening.ConfigureAwait(true);
        }
        finally
        {
            RefreshRecentProjects();
            RaiseFreshness();
        }
    }

    private void OnProjectChosen(object? sender, string fwDataPath) => _ = OpenProjectSafelyAsync(fwDataPath);

    private async Task OpenProjectSafelyAsync(string fwDataPath)
    {
        OpenRefusalMessage = null;
        OpenRefusalDetail = null;
        OnPropertyChanged(nameof(OpenRefusalMessage));
        OnPropertyChanged(nameof(OpenRefusalDetail));
        OnPropertyChanged(nameof(HasOpenRefusal));
        try
        {
            await SetProjectAsync(fwDataPath).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            if (string.Equals(Context.ProjectPath, fwDataPath, StringComparison.Ordinal))
            {
                Context.ClearProject();
                Project.ShowChosen(null);
            }
            OpenRefusalMessage = OpenProjectRefusalText;
            OpenRefusalDetail = exception.Message;
            OnPropertyChanged(nameof(OpenRefusalMessage));
            OnPropertyChanged(nameof(OpenRefusalDetail));
            OnPropertyChanged(nameof(HasOpenRefusal));
        }
        RaiseFreshness();
    }

    private void OnBaselineRefreshed(object? sender, EventArgs e) => _reloadAfterRefresh = ReloadAfterRefreshAsync();

    // The Text list and every page's own state belong to the Baseline just captured, not the one before Refresh.
    private async Task ReloadAfterRefreshAsync()
    {
        if (Context.ProjectPath is not { } path) return;
        var generation = _refreshGeneration;
        await Selection.LoadTextsAsync(path).ConfigureAwait(true);
        if (!IsCurrentRefresh(generation, path)) return;
        await Context.PublishBaselineCapturedAsync().ConfigureAwait(true);
    }

    private void OnOfferRerun(object? sender, EventArgs e) => RerunOffered = true;

    private async Task RefreshAsync()
    {
        var generation = ++_refreshGeneration;
        var projectPath = Context.ProjectPath;
        _isRefreshing = true;
        _refreshCancelled = false;
        _refreshed = false;
        RaiseFreshness();
        try
        {
            await Baseline.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            if (!IsCurrentRefresh(generation, projectPath)) return;
            if (Baseline.ShownRefusal is not null) return;
            await _reloadAfterRefresh.ConfigureAwait(true);
            if (!IsCurrentRefresh(generation, projectPath) || _refreshCancelled ||
                !Assess.RunCommand.CanExecute(null)) return;

            // The run this Refresh starts is the rerun a fresh Baseline would otherwise offer.
            RerunOffered = false;
            await Assess.RunCommand.ExecuteAsync(null).ConfigureAwait(true);
            if (IsCurrentRefresh(generation, projectPath)) _refreshed = Assess.State == RunState.Completed;
        }
        finally
        {
            if (generation == _refreshGeneration)
            {
                _isRefreshing = false;
                RaiseFreshness();
            }
        }
    }

    private void InvalidateRefreshForProjectSwitch()
    {
        _refreshGeneration++;
        _refreshCancelled = true;
        if (!_isRefreshing) return;
        _isRefreshing = false;
        RaiseFreshness();
    }

    private bool IsCurrentRefresh(int generation, string? projectPath) =>
        generation == _refreshGeneration && projectPath is not null &&
        string.Equals(projectPath, Context.ProjectPath, StringComparison.Ordinal);

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
        OnPropertyChanged(nameof(HasRefreshRefusal));
        OnPropertyChanged(nameof(ProjectSwitchEnabled));
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

    private void OnBaselinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Context.ProjectPath is null) Context.Baseline = null;
        else PublishBaseline();
        RaiseFreshness();
    }

    private void PublishBaseline() => Context.Baseline = new WorkspaceBaseline(
        Baseline.HasBaseline, Baseline.CapturedTimeText, Baseline.SavedText, Baseline.CapturedAtText,
        Baseline.HeldStatusText, Baseline.ShownRefusal?.Sentence)
    {
        FieldWorksHeldProject = Baseline.FieldWorksHeldProject,
        SourceLastWriteUtc = Baseline.SourceLastWriteUtc,
        ProjectLastWriteUtc = Baseline.ProjectLastWriteUtc,
    };

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
                OnPropertyChanged(nameof(ProjectSwitchEnabled));
                break;
        }
    }

    // Freshness describes the evidence on screen, whether a run just produced it or the store held it.
    private void OnEvidencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectEvidence.HasAssessment) && Context.HasEvidence)
            Baseline.HasAssessment = true;
        if (e.PropertyName == nameof(ProjectEvidence.Freshness)) RaiseFreshness();
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
            OnPropertyChanged(nameof(ProjectSwitchEnabled));
            // A run started from Refresh leaves the person where they are; the top bar says how it is going.
            if (Assess.IsActive && !_isRefreshing &&
                !(Assess.LastRunWasRerun && Context.CurrentPage == WorkspacePage.Timing))
                Context.OpenPage(WorkspacePage.Texts);
        }

        if (_isRefreshing) RaiseFreshness();

        // A restored stored Assessment completes the run too; only a result not already shown is a new run.
        if (e.PropertyName == nameof(AssessViewModel.State) && Assess.State == RunState.Completed &&
            Assess.Result is { } result && !ReferenceEquals(result, Context.Evidence.Assessment?.Assessment))
        {
            Context.PublishEvidence(new WorkspaceEvidence(result, Assess.CompletedAt, Assess.LastRunWasRerun));
        }
    }

    private async Task AcceptRerunAsync()
    {
        RerunOffered = false;
        if (Assess.RunCommand.CanExecute(null)) await Assess.RunCommand.ExecuteAsync(null);
    }

    /// <summary>Cancels and awaits any active run, so nothing keeps running past this workspace's lifetime.</summary>
    public async ValueTask DisposeAsync()
    {
        await Assess.DisposeAsync().ConfigureAwait(true);
        await Context.StopPageWorkAsync().ConfigureAwait(true);
    }
}
