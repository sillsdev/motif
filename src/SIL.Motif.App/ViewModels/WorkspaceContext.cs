using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The evidence the pages show: the Assessment that completed, when it completed, and whether it re-ran some words
/// rather than measuring the whole Selection again.
/// </summary>
/// <param name="Assessment">The completed Assessment's result.</param>
/// <param name="CompletedAt">When it completed, or <see langword="null"/> when that is not known.</param>
/// <param name="WasRerun">Whether it gave some words more time rather than measuring the whole Selection.</param>
public sealed record WorkspaceEvidence(AssessCommandResponse Assessment, DateTimeOffset? CompletedAt, bool WasRerun)
{
    /// <summary>The FieldWorks save the Assessment's numbers were measured against.</summary>
    public DateTimeOffset MeasuredSaveUtc => Assessment.Baseline.SourceLastWriteUtc;
}

/// <summary>The open project's Baseline as the window describes it, captured whenever it changes.</summary>
/// <param name="HasBaseline">Whether a Baseline has been captured for the project.</param>
/// <param name="CapturedTimeText">What to say about capture when there is no Baseline yet.</param>
/// <param name="SavedText">Which FieldWorks save the Baseline was captured from.</param>
/// <param name="CapturedAtText">When the Baseline was captured.</param>
/// <param name="HeldStatusText">Whether FieldWorks held the project open during the capture.</param>
/// <param name="RefusalMessage">Why the last capture was refused, or <see langword="null"/>.</param>
public sealed record WorkspaceBaseline(
    bool HasBaseline, string CapturedTimeText, string SavedText, string CapturedAtText, string HeldStatusText,
    string? RefusalMessage)
{
    /// <summary>Whether FieldWorks holds the project open, preventing a direct Apply.</summary>
    public bool FieldWorksHeldProject { get; init; }
}

/// <summary>The grammar check's findings in one line, for pages that summarise them.</summary>
/// <param name="SummaryText">The check's state or its count of findings.</param>
/// <param name="ShowFindings">Whether the check found anything to break down.</param>
/// <param name="BreakdownText">The findings by kind.</param>
public sealed record GrammarSummary(string SummaryText, bool ShowFindings, string BreakdownText);

/// <summary>
/// A request to open a page, carrying whatever that page should show when it opens. Each page declares the
/// requests it answers beside its own model, so a new request never touches another page.
/// </summary>
/// <param name="Page">The page the request opens.</param>
public abstract record PageRequest(WorkspacePage Page);

/// <summary>
/// The project and evidence published to the window's pages, their shared Assessment and Selection, pending
/// changes, and navigation actions.
/// </summary>
/// <remarks>
/// The shell publishes project and Assessment changes here. Page models receive those changes and may share
/// <see cref="Assess"/> and <see cref="Selection"/> when their controls operate on the same run. Navigation
/// requests, such as <see cref="OpenWord"/>, reach page models without giving them the shell.
/// </remarks>
public sealed partial class WorkspaceContext : ObservableObject, IProjectStateParticipant
{
    public WorkspaceContext(
        SelectionViewModel selection, AssessViewModel assess, ChangesViewModel changes, ICommandClient commands, IHandoffFolderPicker folderPicker,
        IFileDragSource dragSource, BaselineViewModel baseline)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(assess);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(dragSource);
        ArgumentNullException.ThrowIfNull(baseline);
        Selection = selection;
        Assess = assess;
        Changes = changes;
        Commands = commands;
        FolderPicker = folderPicker;
        DragSource = dragSource;
        Assess.PropertyChanged += OnAssessPropertyChanged;
        _projectParticipants.Add(baseline);
        _projectParticipants.Add(selection);
        _projectParticipants.Add(Changes);
        _projectParticipants.Add(this);
    }

    /// <summary>What the one Assessment run measures; the Texts page edits it and the shell summarises it.</summary>
    public SelectionViewModel Selection { get; }

    /// <summary>
    /// The one Assessment run. It is shared on purpose: the shell's Refresh starts it and shows its progress, the
    /// Texts page starts and renders it, and a completed run is what <see cref="PublishEvidence"/> announces.
    /// </summary>
    public AssessViewModel Assess { get; }

    /// <summary>The machine's Known projects, which the shell keeps current.</summary>
    public ObservableCollection<KnownProjectSummary> KnownProjects { get; init; } = [];

    /// <summary>The shell's action that browses for a project and opens it.</summary>
    public IAsyncRelayCommand? BrowseForProjectCommand { get; init; }

    /// <summary>The shell's action that opens the project at the path given as the parameter.</summary>
    public IAsyncRelayCommand<string>? OpenProjectCommand { get; init; }

    /// <summary>The shell's action that captures a new Baseline from FieldWorks' last save.</summary>
    public IAsyncRelayCommand? RefreshBaselineCommand { get; init; }

    /// <summary>Where a page asks a person to choose a folder.</summary>
    public IHandoffFolderPicker FolderPicker { get; }

    /// <summary>How a page lets a person drag files out of the window.</summary>
    public IFileDragSource DragSource { get; }

    /// <summary>The changes collected on any page and not applied yet; the Review changes page lists them.</summary>
    public ChangesViewModel Changes { get; }

    /// <summary>Whether applying changes has made the visible numbers older than the saved project.</summary>
    [ObservableProperty]
    private bool _appliedSinceRefresh;

    /// <summary>The command seam a page runs its own queries through.</summary>
    public ICommandClient Commands { get; }

    /// <summary>The project setup dialog displayed over the current page.</summary>
    public SetupViewModel? Setup { get; private set; }

    /// <summary>The open project's <c>.fwdata</c> path, or <see langword="null"/> before one is chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    [NotifyPropertyChangedFor(nameof(ProjectName))]
    private string? _projectPath;

    /// <summary>Whether a project has been chosen.</summary>
    public bool HasProject => ProjectPath is not null;

    /// <summary>The open project's file name, or a prompt before one is chosen.</summary>
    public string ProjectName => ProjectPath is null ? "Choose a project" : Path.GetFileName(ProjectPath);

    /// <summary>The open project's Baseline, as the shell last published it.</summary>
    [ObservableProperty]
    private WorkspaceBaseline? _baseline;

    /// <summary>The grammar check in one line, as the page that owns the check last published it.</summary>
    [ObservableProperty]
    private GrammarSummary? _grammarSummary;

    /// <summary>The evidence published for the open project, or <see langword="null"/> before any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEvidence))]
    [NotifyPropertyChangedFor(nameof(HasNoEvidence))]
    private WorkspaceEvidence? _evidence;

    /// <summary>The current stored read model published for the open project.</summary>
    [ObservableProperty]
    private CurrentEvidenceSnapshot? _currentEvidence;

    /// <summary>Whether any evidence has been published for the open project.</summary>
    public bool HasEvidence => Evidence is not null;

    /// <summary>The negation of <see cref="HasEvidence"/>, so a view never composes <c>!</c> itself.</summary>
    public bool HasNoEvidence => Evidence is null;

    /// <summary>Whether the project and Selection controls accept input: not while an Assessment runs.</summary>
    public bool ProjectAndSelectionEnabled => !Assess.IsActive;

    /// <summary>The page the window is showing.</summary>
    [ObservableProperty]
    private WorkspacePage _currentPage;

    private readonly List<PageModel> _pages = [];
    private readonly List<IProjectStateParticipant> _projectParticipants = [];
    private CancellationTokenSource? _openCancellation;
    private int _openGeneration;

    // Called by each page model's constructor, so the context reaches a page only through its hooks.
    internal void Attach(PageModel page)
    {
        _pages.Add(page);
        _projectParticipants.Add(page);
    }

    /// <summary>Registers the setup dialog that is shared by the shell.</summary>
    internal void AttachSetup(SetupViewModel setup)
    {
        Setup = setup;
        _projectParticipants.Add(setup);
    }

    /// <summary>Forgets the evidence and tells every page to drop what it showed for the previous project.</summary>
    internal void ClearProject()
    {
        Interlocked.Increment(ref _openGeneration);
        Interlocked.Exchange(ref _openCancellation, null)?.Cancel();
        ClearOwnProjectState();
        foreach (var participant in _projectParticipants.Where(participant => !ReferenceEquals(participant, this))
                     .Reverse().ToArray())
            participant.ClearProject();
    }

    private void ClearOwnProjectState()
    {
        Assess.Reset();
        Evidence = null;
        CurrentEvidence = null;
        Baseline = null;
        GrammarSummary = null;
        AppliedSinceRefresh = false;
        CurrentPage = WorkspacePage.Overview;
        ProjectPath = null;
        Assess.ProjectPath = null;
    }

    /// <summary>Clears and opens every project-bound participant before returning.</summary>
    public async Task OpenProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var generation = Interlocked.Increment(ref _openGeneration);
        var openCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var superseded = Interlocked.Exchange(ref _openCancellation, openCancellation);
        superseded?.Cancel();
        var participants = _projectParticipants.ToArray();
        try
        {
            await StopProjectWorkAsync().ConfigureAwait(true);
            if (!IsCurrentOpen(generation)) return;
            openCancellation.Token.ThrowIfCancellationRequested();

            ((IProjectStateParticipant)this).ClearProject();
            foreach (var participant in participants.Where(participant => !ReferenceEquals(participant, this))
                         .Reverse())
                participant.ClearProject();
            if (!IsCurrentOpen(generation)) return;
            openCancellation.Token.ThrowIfCancellationRequested();

            foreach (var stage in Enum.GetValues<ProjectOpenStage>())
            {
                if (!IsCurrentOpen(generation)) return;
                openCancellation.Token.ThrowIfCancellationRequested();
                try
                {
                    await Task.WhenAll(participants.Where(participant => participant.OpenStage == stage)
                        .Select(participant => participant.OpenProjectAsync(projectPath, openCancellation.Token)))
                        .ConfigureAwait(true);
                }
                catch (OperationCanceledException) when (!IsCurrentOpen(generation))
                {
                    return;
                }
                catch (Exception) when (!IsCurrentOpen(generation))
                {
                    return;
                }
                if (!IsCurrentOpen(generation)) return;
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _openCancellation, null, openCancellation);
            openCancellation.Dispose();
        }
    }

    /// <summary>Tells every page a new Baseline was captured, and returns once each has reloaded.</summary>
    public async Task PublishBaselineCapturedAsync(CancellationToken cancellationToken = default)
    {
        await Changes.ReloadAsync(cancellationToken).ConfigureAwait(true);
        foreach (var page in _pages.ToArray())
            await page.BaselineCapturedAsync(cancellationToken).ConfigureAwait(true);
        if (Setup is not null) await Setup.BaselineCapturedAsync().ConfigureAwait(true);
    }

    /// <summary>Stops whatever work any page has running, and returns once each has stopped.</summary>
    public async Task StopPageWorkAsync()
    {
        foreach (var page in _pages.ToArray()) await page.StopWorkAsync().ConfigureAwait(true);
    }

    private async Task StopProjectWorkAsync()
    {
        // Await each command's unwind; its page model outlives this project.
        if (Assess.IsActive)
        {
            Assess.CancelCommand.Execute(null);
            if (Assess.RunCommand.ExecutionTask is { } running) await running.ConfigureAwait(true);
        }
        await StopPageWorkAsync().ConfigureAwait(true);
    }

    private bool IsCurrentOpen(int generation) => generation == Volatile.Read(ref _openGeneration);

    /// <summary>Publishes <paramref name="evidence"/> as what every page now shows.</summary>
    public void PublishEvidence(WorkspaceEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Evidence = evidence;
        AppliedSinceRefresh = false;
        Changes.AssessmentId = evidence.Assessment.Measurements
            .SingleOrDefault(measurement => measurement.Kind == "ParseTime")?.AssessmentId;
        foreach (var page in _pages.ToArray()) page.EvidencePublished(evidence);
    }

    /// <summary>Publishes the stored read model to the open project's pages.</summary>
    public async Task PublishCurrentEvidenceAsync(
        CurrentEvidenceSnapshot evidence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        CurrentEvidence = evidence;
        if (Evidence is null && StoredAssessmentView.From(evidence) is { } restored)
        {
            Assess.Restore(restored);
            Evidence = restored;
            Changes.AssessmentId = evidence.MatchingAssessment?.AssessmentId;
        }
        foreach (var page in _pages.ToArray())
            await page.CurrentEvidencePublishedAsync(evidence, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Notifies pages after a grammar check has updated the stored warning summary.</summary>
    public async Task PublishGrammarCheckedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var page in _pages.ToArray())
            await page.GrammarCheckedAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens <paramref name="page"/> as it stands.</summary>
    public void OpenPage(WorkspacePage page) => CurrentPage = page;

    /// <summary>Lets the page <paramref name="request"/> names answer it, then opens that page.</summary>
    public void Open(PageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var page in _pages.ToArray()) page.Requested(request);
        CurrentPage = request.Page;
    }

    /// <summary>Opens the Texts page on <paramref name="tab"/>.</summary>
    public void OpenTexts(TextsTab tab) => Open(new OpenTextsRequest(tab));

    /// <summary>Opens <paramref name="word"/> in the Texts page's word list, with every filter cleared.</summary>
    public void OpenWord(string word) => Open(new OpenWordRequest(word));

    /// <summary>Opens Try a Word on <paramref name="word"/> and traces it straight away.</summary>
    public void TryWord(string word) => Open(new TryWordRequest(word));

    /// <summary>Opens the Timing page on <paramref name="words"/>, filtered to <paramref name="rule"/> when named.</summary>
    public void OpenTiming(IReadOnlyList<string> words, string? rule) => Open(new OpenTimingRequest(words, rule));

    /// <summary>Opens the AI Handoff page with <paramref name="words"/> as the words it will write.</summary>
    public void HandOff(IReadOnlyList<string> words) => Open(new HandOffRequest(words));

    private void OnAssessPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssessViewModel.IsActive)) OnPropertyChanged(nameof(ProjectAndSelectionEnabled));
    }

    ProjectOpenStage IProjectStateParticipant.OpenStage => ProjectOpenStage.Context;

    void IProjectStateParticipant.ClearProject() => ClearOwnProjectState();

    Task IProjectStateParticipant.OpenProjectAsync(string projectPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProjectPath = projectPath;
        Assess.ProjectPath = projectPath;
        return Task.CompletedTask;
    }

}
