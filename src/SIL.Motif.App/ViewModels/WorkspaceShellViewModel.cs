using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Projection.Usage;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Motif window's shell: choosing a project cancels whatever Assessment or AI Handoff run is active, clears
/// the state the previous project displayed, and loads the new project's Baseline and Selection. It owns the
/// sidebar, project menu, and freshness line, and publishes project and Assessment changes through
/// <see cref="Context"/> to the page models.
/// </summary>
/// <remarks>
/// Page models load their own project queries and handle requests through the context. Refresh captures a
/// Baseline; <see cref="ParseAllWordsCommand"/> measures the saved Default Selection against it.
/// </remarks>
public sealed partial class WorkspaceShellViewModel : ObservableObject, IAsyncDisposable
{
    private const string OpenProjectRefusalText = "Motif could not open this project.";

    /// <summary>What the window asks before it deletes a store another version of Motif made.</summary>
    public const string StoreDeletionWarning =
        "Changes not applied yet are lost. Your FieldWorks project is not touched.";

    /// <summary>What the project menu's Configure entry says while the project has no Baseline to choose Texts from.</summary>
    public const string ConfigureNeedsBaselineText = "Refresh first to choose Texts";

    /// <summary>Below this window width the sidebar shows icons alone, with each label as a tooltip.</summary>
    public const double SidebarCollapseWidth = 1100;

    private readonly ICommandClient _commandClient;
    private string? _storeDeletionProject;
    private Task _reloadAfterRefresh = Task.CompletedTask;
    private Task? _freshnessCheckTask;
    private bool _isRefreshing;
    private bool _refreshed;
    [ObservableProperty]
    private bool _isParsingAllWords;
    private Task? _knownProjectsRefreshTask;
    private int _refreshGeneration;

    public WorkspaceShellViewModel(
        ProjectViewModel project, BaselineViewModel baseline, SelectionViewModel selection, AssessViewModel assess, IHandoffFolderPicker folderPicker, IFileDragSource dragSource,
        ICommandClient commandClient, TimeProvider? clock = null, IClipboard? clipboard = null,
        IDiagnosticFilePicker? diagnosticFiles = null, IDiagnosticWindowDialogs? diagnosticDialogs = null,
        TechDemoNoticeViewModel? techDemoNotice = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(assess);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(dragSource);
        ArgumentNullException.ThrowIfNull(commandClient);

        _commandClient = commandClient;
        Project = project;
        Baseline = baseline;
        TechDemoNotice = techDemoNotice;
        Context = new WorkspaceContext(selection, assess, new ChangesViewModel(commandClient), commandClient, folderPicker,
            dragSource, baseline, clock, clipboard, diagnosticFiles, diagnosticDialogs)
        {
            KnownProjects = project.KnownProjects,
            BrowseForProjectCommand = project.BrowseCommand,
            OpenProjectCommand = new AsyncRelayCommand<string>(path =>
                path is null ? Task.CompletedTask : OpenProjectFromCommandAsync(path)),
        };
        PublishBaseline();
        ParseAllWordsCommand = new AsyncRelayCommand(ParseAllWordsAsync, CanParseAllWords);
        Context.ParseAllWordsCommand = ParseAllWordsCommand;
        Pages = PageRegistry.Entries
            .Select(entry => new PageViewModel(entry.Page, entry.Title, entry.Icon, entry.CreateModel(Context)))
            .ToArray();
        var setup = new SetupViewModel(Context, PageModel<TextsPageModel>().Words);
        Context.AttachSetup(setup);
        setup.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SetupViewModel.CanRunDefaultSelection) ||
                e.PropertyName == nameof(SetupViewModel.IsOpen))
            {
                OnPropertyChanged(nameof(ShowsParseAllWordsAction));
                OnPropertyChanged(nameof(ShowsChooseWhatToParseAction));
                ParseAllWordsCommand.NotifyCanExecuteChanged();
            }
        };
        OpenConfiguration = setup.OpenForConfiguration;
        ShowPageCommand = new RelayCommand<WorkspacePage>(Context.OpenPage);

        SelectNewProjectCommand = new AsyncRelayCommand(() => Project.BrowseCommand.ExecuteAsync(null));
        OpenRecentProjectCommand = new AsyncRelayCommand<RecentProjectViewModel>(recent =>
            recent is null ? Task.CompletedTask : OpenProjectFromCommandAsync(recent.FullFwDataPath));
        ConfigureCommand = new AsyncRelayCommand(async () =>
        {
            using var usageAction = _commandClient.BeginUsageAction("config show",
                UsageArgumentShape.Text("fwDataPath"));
            OpenConfiguration?.Invoke();
            if (Context.Setup?.ConfigurationLoadTask is { } load) await load.ConfigureAwait(true);
        }, () => CanConfigure);
        Context.ConfigureCommand = ConfigureCommand;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => HasProject && !_isRefreshing && !Assess.IsActive);
        Context.RefreshProjectCommand = RefreshCommand;
        SeeWhatChangedCommand = new RelayCommand(() => Context.OpenTexts(TextsTab.WhatChanged), () => ShowsSeeWhatChanged);
        DeleteRefusedStoreCommand = new RelayCommand<WindowRefusal>(AskToDeleteRefusedStore, CanAskToDeleteRefusedStore);
        ConfirmStoreDeletionCommand = new AsyncRelayCommand(DeleteRefusedStoreAndReopenAsync,
            () => IsConfirmingStoreDeletion);
        CancelStoreDeletionCommand = new RelayCommand(() => StopConfirmingStoreDeletion(), () => IsConfirmingStoreDeletion);

        Project.ProjectChosen += OnProjectChosen;
        Project.KnownProjects.CollectionChanged += OnKnownProjectsChanged;
        Baseline.Refreshed += OnBaselineRefreshed;
        Baseline.PropertyChanged += OnBaselinePropertyChanged;
        Assess.PropertyChanged += OnAssessPropertyChanged;
        Context.PropertyChanged += OnContextPropertyChanged;
        Context.Evidence.PropertyChanged += OnEvidencePropertyChanged;

        RefreshPages();
    }

    /// <summary>The tech demo notice shown until the person acknowledges it, or <see langword="null"/> when disabled.</summary>
    public TechDemoNoticeViewModel? TechDemoNotice { get; }

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

    /// <summary>Re-reads the Known projects, keeping the last list if the read fails.</summary>
    public Task RefreshKnownProjectsAsync()
    {
        if (_knownProjectsRefreshTask is { IsCompleted: false } inProgress) return inProgress;
        return _knownProjectsRefreshTask = RefreshKnownProjectsCoreAsync();
    }

    private async Task RefreshKnownProjectsCoreAsync()
    {
        try
        {
            await Project.LoadKnownProjectsAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // A failed Known projects read should preserve the last usable list.
        }
    }

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

    /// <summary>
    /// Whether the project menu offers Configure: only once the open project has a Baseline, which a Refresh
    /// captures. Pinned by `WithoutABaselineConfigureIsUnavailableAndSaysToRefreshFirst`.
    /// </summary>
    public bool CanConfigure => HasProject && Context.Baseline?.HasBaseline == true;

    /// <summary>The Configure entry's second line: what it changes, or what to do first when it cannot open yet.</summary>
    public string ConfigureDetailText => HasProject && !CanConfigure
        ? ConfigureNeedsBaselineText
        : "Texts, added words and limits";

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

    /// <summary>Why the last attempt to open a project failed, in the window's words.</summary>
    public WindowRefusal? OpenRefusal { get; private set; }

    /// <summary>Whether the last attempt to open a project was refused.</summary>
    public bool HasOpenRefusal => OpenRefusal is not null;

    /// <summary>
    /// Delete this file and reopen: asks, with <see cref="StoreDeletionWarning"/>, before deleting the store the
    /// refusal passed as the parameter names. Only a <see cref="WindowRefusal.OffersStoreDeletion"/> refusal can.
    /// </summary>
    public IRelayCommand<WindowRefusal> DeleteRefusedStoreCommand { get; }

    /// <summary>Whether the window is asking the person to confirm deleting the refused store.</summary>
    public bool IsConfirmingStoreDeletion => _storeDeletionProject is not null;

    /// <summary>Deletes the refused store of the project on screen, then opens that project again.</summary>
    public IAsyncRelayCommand ConfirmStoreDeletionCommand { get; }

    /// <summary>Stops asking, and deletes nothing.</summary>
    public IRelayCommand CancelStoreDeletionCommand { get; }

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
                ProjectFreshness.Refreshing => "Capturing a new Baseline...",
                ProjectFreshness.Refreshed when Context.NeedsAssessment =>
                    "Refreshed. Parse all words to update the numbers.",
                ProjectFreshness.Refreshed => Assess.Difference.HasDifference
                    ? Assess.Difference.Summary
                    : "The words you chose have been parsed.",
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

    /// <summary>Whether the top row offers Refresh instead of parsing or showing parse progress.</summary>
    public bool ShowsRefreshAction => !FreshnessIsBusy && !IsParsingAllWords && !Assess.IsActive &&
        (!Context.NeedsAssessment || FreshnessIsStale);

    /// <summary>Whether the top row offers the saved Default Selection against the current Baseline.</summary>
    public bool ShowsParseAllWordsAction => ShowsParseAction && Context.Setup?.CanRunDefaultSelection == true;

    /// <summary>Whether the top row offers to open Configure when there is no saved Selection to parse.</summary>
    public bool ShowsChooseWhatToParseAction => ShowsParseAction && Context.Setup?.CanRunDefaultSelection == false;

    /// <summary>Whether the top row is showing progress for an Assessment.</summary>
    public bool ShowsParseAllWordsProgress => Assess.IsActive;

    /// <summary>The current number of words parsed, or the current run stage while no count is available.</summary>
    public string ParseAllWordsProgressText => Assess.Progress is
        { Stage: AssessmentStage.Parsing, Total: { } total } progress
        ? $"Parsing {progress.Completed:N0} of {total:N0} words"
        : Assess.Progress?.Message is { Length: > 0 } message ? message : "Preparing to parse words...";

    /// <summary>Whether parse progress has no count to display yet.</summary>
    public bool ParseAllWordsProgressIsIndeterminate => Assess.Progress?.Total is null;

    /// <summary>The fraction of the parser's reported word count that is complete.</summary>
    public double ParseAllWordsProgressFraction => Assess.ProgressFraction;

    /// <summary>Whether a finished Refresh moved some words, so there is something to see.</summary>
    public bool ShowsSeeWhatChanged => Freshness == ProjectFreshness.Refreshed && Assess.Difference.HasDifference;

    /// <summary>Captures a new Baseline from FieldWorks' last save.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Opens what a finished Refresh changed.</summary>
    public IRelayCommand SeeWhatChangedCommand { get; }

    /// <summary>Measures the saved Default Selection against the current Baseline.</summary>
    public IAsyncRelayCommand ParseAllWordsCommand { get; }

    /// <summary>
    /// Reads the recorded Baseline, the project file's last-write time, the pending changes and the stored
    /// evidence again, so a save FieldWorks made, or an Assessment recorded, while the window was elsewhere shows at
    /// once. Reads only; nothing reruns.
    /// </summary>
    public Task CheckFreshnessAsync(CancellationToken cancellationToken = default)
    {
        if (_freshnessCheckTask is { IsCompleted: false }) return _freshnessCheckTask;
        if (!HasProject || _isRefreshing) return Task.CompletedTask;
        return _freshnessCheckTask = ReadFreshnessAsync(cancellationToken);
    }

    private async Task ReadFreshnessAsync(CancellationToken cancellationToken)
    {
        await Baseline.CheckAsync(cancellationToken).ConfigureAwait(true);
        await Context.Changes.ReloadAsync(cancellationToken).ConfigureAwait(true);
        if (Context.ProjectPath is { } projectPath && ProjectReconciliationMarker.Exists(projectPath))
            PageModel<ReviewPageModel>().ShowReconciliationNeeded();
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


    /// <summary>
    /// Cancels any active Assessment or AI Handoff run, clears whatever the previous project displayed, and
    /// loads the newly chosen project's Baseline and Text state.
    /// </summary>
    internal async Task SetProjectAsync(string fwDataPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);
        InvalidateRefreshForProjectSwitch();
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

    private bool CanAskToDeleteRefusedStore(WindowRefusal? refusal) =>
        refusal is { OffersStoreDeletion: true } && Context.ProjectPath is not null && !IsConfirmingStoreDeletion;

    private void AskToDeleteRefusedStore(WindowRefusal? refusal)
    {
        if (!CanAskToDeleteRefusedStore(refusal)) return;
        SetStoreDeletionProject(Context.ProjectPath);
    }

    private void StopConfirmingStoreDeletion() => SetStoreDeletionProject(null);

    private void SetStoreDeletionProject(string? projectPath)
    {
        _storeDeletionProject = projectPath;
        OnPropertyChanged(nameof(IsConfirmingStoreDeletion));
        DeleteRefusedStoreCommand.NotifyCanExecuteChanged();
        ConfirmStoreDeletionCommand.NotifyCanExecuteChanged();
        CancelStoreDeletionCommand.NotifyCanExecuteChanged();
    }

    // The project is the one asked about, so a project switched to since can never lose its store.
    private async Task DeleteRefusedStoreAndReopenAsync()
    {
        if (_storeDeletionProject is not { } projectPath) return;
        using var usageAction = _commandClient.BeginUsageAction("store delete-refused",
            UsageArgumentShape.Text("fwDataPath"));
        StopConfirmingStoreDeletion();
        if (!string.Equals(projectPath, Context.ProjectPath, StringComparison.Ordinal)) return;
        var outcome = await _commandClient.DeleteRefusedStoreAsync(
            new ProjectStoreResetRequest(projectPath), CancellationToken.None).ConfigureAwait(true);
        if (outcome.Refusal is { } refusal)
        {
            ShowOpenRefusal(WindowRefusal.From(refusal));
            return;
        }
        await OpenProjectSafelyAsync(projectPath).ConfigureAwait(true);
    }

    private void ShowOpenRefusal(WindowRefusal? refusal)
    {
        OpenRefusal = refusal;
        OnPropertyChanged(nameof(OpenRefusal));
        OnPropertyChanged(nameof(HasOpenRefusal));
    }

    private async Task OpenProjectFromCommandAsync(string fwDataPath)
    {
        using var usageAction = _commandClient.BeginUsageAction("project open",
            UsageArgumentShape.Text("fwDataPath"));
        await OpenProjectSafelyAsync(fwDataPath).ConfigureAwait(true);
    }

    private async Task OpenProjectSafelyAsync(string fwDataPath)
    {
        StopConfirmingStoreDeletion();
        ShowOpenRefusal(null);
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
            ShowOpenRefusal(WindowRefusal.Failure(WindowRefusal.OpenFailedCode, OpenProjectRefusalText, exception));
        }
        RaiseFreshness();
    }

    private void OnBaselineRefreshed(object? sender, EventArgs e)
    {
        Context.ClearAssessmentForNewBaseline();
        _reloadAfterRefresh = ReloadAfterRefreshAsync();
    }

    // The Text list and every page's own state belong to the Baseline just captured, not the one before Refresh.
    private async Task ReloadAfterRefreshAsync()
    {
        if (Context.ProjectPath is not { } path) return;
        var generation = _refreshGeneration;
        await Selection.LoadTextsAsync(path).ConfigureAwait(true);
        if (!IsCurrentRefresh(generation, path)) return;
        await Context.PublishBaselineCapturedAsync().ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        var generation = ++_refreshGeneration;
        var projectPath = Context.ProjectPath;
        _isRefreshing = true;
        _refreshed = false;
        RaiseFreshness();
        try
        {
            await Baseline.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            if (!IsCurrentRefresh(generation, projectPath)) return;
            if (Baseline.ShownRefusal is not null) return;
            await _reloadAfterRefresh.ConfigureAwait(true);
            if (!IsCurrentRefresh(generation, projectPath) || projectPath is null) return;
            if (!ProjectReconciliationMarker.Exists(projectPath) || ProjectReconciliationMarker.Clear(projectPath))
                PageModel<ReviewPageModel>().ClearReconciliationNeeded();
            _refreshed = true;
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
        if (!_isRefreshing) return;
        _isRefreshing = false;
        RaiseFreshness();
    }

    private bool IsCurrentRefresh(int generation, string? projectPath) =>
        generation == _refreshGeneration && projectPath is not null &&
        string.Equals(projectPath, Context.ProjectPath, StringComparison.Ordinal);

    private bool ShowsParseAction => !FreshnessIsBusy && !IsParsingAllWords && Context.NeedsAssessment &&
        !FreshnessIsStale && !Assess.IsActive && Context.Setup is { IsOpen: false };

    private bool CanParseAllWords() => Context.NeedsAssessment && !FreshnessIsStale && !_isRefreshing &&
        !Assess.IsActive && Context.Setup is { IsOpen: false, CanRunDefaultSelection: true };

    private async Task ParseAllWordsAsync()
    {
        if (!CanParseAllWords() || Context.Setup is not { } setup) return;
        IsParsingAllWords = true;
        RaiseFreshness();
        try
        {
            await setup.RunDefaultSelectionAsync().ConfigureAwait(true);
        }
        finally
        {
            IsParsingAllWords = false;
            ParseAllWordsCommand.NotifyCanExecuteChanged();
            RaiseFreshness();
        }
    }

    partial void OnIsParsingAllWordsChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowsRefreshAction));
        OnPropertyChanged(nameof(ShowsParseAllWordsAction));
        OnPropertyChanged(nameof(ShowsChooseWhatToParseAction));
        OnPropertyChanged(nameof(ShowsParseAllWordsProgress));
        ParseAllWordsCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(ShowsRefreshAction));
        OnPropertyChanged(nameof(ShowsParseAllWordsAction));
        OnPropertyChanged(nameof(ShowsChooseWhatToParseAction));
        OnPropertyChanged(nameof(ShowsParseAllWordsProgress));
        OnPropertyChanged(nameof(ParseAllWordsProgressText));
        OnPropertyChanged(nameof(ParseAllWordsProgressIsIndeterminate));
        OnPropertyChanged(nameof(ParseAllWordsProgressFraction));
        RefreshCommand.NotifyCanExecuteChanged();
        ParseAllWordsCommand.NotifyCanExecuteChanged();
        DeleteRefusedStoreCommand.NotifyCanExecuteChanged();
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
                RaiseConfigure();
                break;
            case nameof(WorkspaceContext.Baseline):
                RaiseConfigure();
                break;
            case nameof(WorkspaceContext.NeedsAssessment):
                OnPropertyChanged(nameof(ShowsRefreshAction));
                OnPropertyChanged(nameof(ShowsParseAllWordsAction));
                OnPropertyChanged(nameof(ShowsChooseWhatToParseAction));
                ParseAllWordsCommand.NotifyCanExecuteChanged();
                break;
        }
    }

    private void RaiseConfigure()
    {
        OnPropertyChanged(nameof(CanConfigure));
        OnPropertyChanged(nameof(ConfigureDetailText));
        ConfigureCommand.NotifyCanExecuteChanged();
    }

    // Freshness describes the evidence on screen, whether a run just produced it or the store held it.
    private void OnEvidencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
            OnPropertyChanged(nameof(ShowsRefreshAction));
            OnPropertyChanged(nameof(ShowsParseAllWordsAction));
            OnPropertyChanged(nameof(ShowsChooseWhatToParseAction));
            OnPropertyChanged(nameof(ShowsParseAllWordsProgress));
            // Shell-started parsing keeps the current page open while manual runs open Texts.
            if (Assess.IsActive && !_isRefreshing && !IsParsingAllWords &&
                !(Assess.LastRunWasRerun && Context.CurrentPage == WorkspacePage.Timing))
                Context.OpenPage(WorkspacePage.Texts);
            ParseAllWordsCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(AssessViewModel.Progress))
        {
            OnPropertyChanged(nameof(ParseAllWordsProgressText));
            OnPropertyChanged(nameof(ParseAllWordsProgressIsIndeterminate));
            OnPropertyChanged(nameof(ParseAllWordsProgressFraction));
        }

        if (_isRefreshing) RaiseFreshness();

        // A restored stored Assessment completes the run too; only a result not already shown is a new run.
        if (e.PropertyName == nameof(AssessViewModel.State) && Assess.State == RunState.Completed &&
            Assess.Result is { } result && !ReferenceEquals(result, Context.Evidence.Assessment?.Assessment))
        {
            Context.PublishEvidence(new WorkspaceEvidence(result, Assess.CompletedAt, Assess.LastRunWasRerun));
        }
    }

    /// <summary>Cancels and awaits any active run, so nothing keeps running past this workspace's lifetime.</summary>
    public async ValueTask DisposeAsync()
    {
        await Assess.DisposeAsync().ConfigureAwait(true);
        await Context.StopPageWorkAsync().ConfigureAwait(true);
        if (_knownProjectsRefreshTask is { } refresh) await refresh.ConfigureAwait(true);
    }
}
