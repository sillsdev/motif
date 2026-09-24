using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;
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
    string? RefusalMessage);

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
/// What every page of the window is built from: the open project, the evidence published for it, the models the
/// pages share, the pending changes, and the navigation actions by which one page opens another.
/// </summary>
/// <remarks>
/// The window's shell publishes each project change and each completed Assessment here exactly once; each
/// <see cref="PageModel"/> reacts to those signals and owns its own display state, so no page reaches through
/// the shell or into another page. A page opens another only by an action here, such as <see cref="OpenWord"/>,
/// which sets <see cref="CurrentPage"/> after the target page has answered the <see cref="PageRequest"/>.
/// </remarks>
public sealed partial class WorkspaceContext : ObservableObject
{
    public WorkspaceContext(
        ProjectViewModel project, ProjectHistoryViewModel projectHistory, BaselineViewModel baseline,
        SelectionViewModel selection, TextWordsViewModel words, AssessViewModel assess,
        ChangesViewModel changes, ICommandClient commands, IHandoffFolderPicker folderPicker,
        IFileDragSource dragSource)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(projectHistory);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(assess);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(folderPicker);
        ArgumentNullException.ThrowIfNull(dragSource);
        Project = project;
        ProjectHistory = projectHistory;
        Baseline = baseline;
        Selection = selection;
        Words = words;
        Assess = assess;
        Changes = changes;
        Commands = commands;
        FolderPicker = folderPicker;
        DragSource = dragSource;
        Assess.PropertyChanged += OnAssessPropertyChanged;
    }

    public ProjectViewModel Project { get; }

    public ProjectHistoryViewModel ProjectHistory { get; }

    public BaselineViewModel Baseline { get; }

    public SelectionViewModel Selection { get; }

    public TextWordsViewModel Words { get; }

    public AssessViewModel Assess { get; }

    /// <summary>Where a page asks a person to choose a folder.</summary>
    public IHandoffFolderPicker FolderPicker { get; }

    /// <summary>How a page lets a person drag files out of the window.</summary>
    public IFileDragSource DragSource { get; }

    /// <summary>The changes collected on any page and not applied yet; the Review changes page lists them.</summary>
    public ChangesViewModel Changes { get; }

    /// <summary>The command seam a page runs its own queries through.</summary>
    public ICommandClient Commands { get; }

    /// <summary>The open project's <c>.fwdata</c> path, or <see langword="null"/> before one is chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    [NotifyPropertyChangedFor(nameof(ProjectName))]
    private string? _projectPath;

    /// <summary>Whether a project has been chosen.</summary>
    public bool HasProject => ProjectPath is not null;

    /// <summary>The open project's file name, or a prompt before one is chosen.</summary>
    public string ProjectName => ProjectPath is null ? "Choose a project" : Path.GetFileName(ProjectPath);

    /// <summary>The grammar check in one line, as the page that owns the check last published it.</summary>
    [ObservableProperty]
    private GrammarSummary? _grammarSummary;

    /// <summary>The evidence published for the open project, or <see langword="null"/> before any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEvidence))]
    [NotifyPropertyChangedFor(nameof(HasNoEvidence))]
    private WorkspaceEvidence? _evidence;

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

    // Called by each page model's constructor, so the context reaches a page only through its hooks.
    internal void Attach(PageModel page) => _pages.Add(page);

    /// <summary>Forgets the evidence and tells every page to drop what it showed for the previous project.</summary>
    public void ClearProject()
    {
        Evidence = null;
        foreach (var page in _pages) page.ProjectCleared();
    }

    /// <summary>Opens <paramref name="projectPath"/> on every page, and returns once each has loaded it.</summary>
    public async Task PublishProjectOpenedAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ProjectPath = projectPath;
        await Changes.OpenProjectAsync(projectPath).ConfigureAwait(true);
        foreach (var page in _pages.ToArray())
            await page.ProjectOpenedAsync(projectPath, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Tells every page a new Baseline was captured, and returns once each has reloaded.</summary>
    public async Task PublishBaselineCapturedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var page in _pages.ToArray())
            await page.BaselineCapturedAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Stops whatever work any page has running, and returns once each has stopped.</summary>
    public async Task StopPageWorkAsync()
    {
        foreach (var page in _pages.ToArray()) await page.StopWorkAsync().ConfigureAwait(true);
    }

    /// <summary>Publishes <paramref name="evidence"/> as what every page now shows.</summary>
    public void PublishEvidence(WorkspaceEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Evidence = evidence;
        foreach (var page in _pages.ToArray()) page.EvidencePublished(evidence);
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
}
