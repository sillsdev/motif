using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
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

    /// <summary>Whether the store supplied it, rather than a run in this window.</summary>
    public bool IsStored { get; init; }
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

    /// <summary>Exact capture identity for checking display hints against returned trace evidence.</summary>
    public SIL.Motif.Contract.Baselines.BaselineToken? Token { get; init; }

    /// <summary>The FieldWorks save the Baseline copies, or <see langword="null"/> before any capture.</summary>
    public DateTimeOffset? SourceLastWriteUtc { get; init; }

    /// <summary>The project file's last-write time as last read, or <see langword="null"/> when unknown.</summary>
    public DateTimeOffset? ProjectLastWriteUtc { get; init; }
}

/// <summary>The grammar check's findings in one line, for pages that summarise them.</summary>
/// <param name="SummaryText">The check's state or its count of findings.</param>
/// <param name="ShowFindings">Whether the check found anything to break down.</param>
/// <param name="BreakdownText">The findings by kind.</param>
public sealed record GrammarSummary(string SummaryText, bool ShowFindings, string BreakdownText)
{
    /// <summary>The stored warning findings, shared with pages that join them to project identities.</summary>
    public IReadOnlyList<GrammarWarning> Findings { get; init; } = [];
}

/// <summary>
/// A request to open a page, carrying whatever that page should show when it opens. Each page declares the
/// requests it answers beside its own model, so a new request never touches another page.
/// </summary>
/// <param name="Page">The page the request opens.</param>
public abstract record PageRequest(WorkspacePage Page);

/// <summary>A trace the inspector was opened from: the word traced, and the Baseline the trace read.</summary>
/// <param name="Word">The traced word.</param>
/// <param name="BaselineDigest">The bundle digest of the Baseline the trace read, or <see langword="null"/> when not recorded.</param>
public sealed record InspectorTrace(string Word, string? BaselineDigest);

/// <summary>One thing a trace recorded about a name, such as its gloss or the rule's outcome.</summary>
/// <param name="Label">What the line is, such as <c>Gloss</c>.</param>
/// <param name="Value">What the trace recorded.</param>
public sealed record InspectorDetail(string Label, string Value)
{
    /// <summary>The lines for <paramref name="pairs"/>, leaving out any the trace did not record.</summary>
    public static IReadOnlyList<InspectorDetail> Recorded(params (string Label, string? Value)[] pairs) =>
        [.. pairs.Where(pair => pair.Value is { Length: > 0 } value && !value.EndsWith(" not recorded", StringComparison.Ordinal))
            .Select(pair => new InspectorDetail(pair.Label, pair.Value!))];
}

/// <summary>
/// A request to show one object in the inspector beside the page: a morpheme, rule or other name, by identity.
/// Unlike a <see cref="PageRequest"/> it leaves the page where it is.
/// </summary>
/// <param name="Subject">The object to show.</param>
public sealed record OpenInspectorRequest(InspectorSubject Subject)
{
    /// <summary>The trace the subject was named in, when it was opened from one; its details are kept apart.</summary>
    public InspectorTrace? Trace { get; init; }

    /// <summary>What that trace itself recorded about the subject, line by line; empty when not opened from a trace.</summary>
    public IReadOnlyList<InspectorDetail> Captured { get; init; } = [];

    /// <summary>
    /// What the inspector's first breadcrumb names, such as the word whose card it opened from; <see langword="null"/>
    /// names the page.
    /// </summary>
    public string? From { get; init; }
}

/// <summary>
/// The project and evidence published to the window's pages, their shared Assessment and Selection, pending
/// changes, and navigation actions.
/// </summary>
/// <remarks>
/// The shell publishes project and Assessment changes here. Page models receive those changes and may share
/// <see cref="Assess"/> and <see cref="Selection"/> when their controls operate on the same run. Navigation
/// requests, such as <see cref="OpenWord"/>, reach page models without giving them the shell. The context reads
/// the stored evidence itself when a project opens and after each Refresh, so no page has to be present for the
/// others to see it.
/// </remarks>
public sealed partial class WorkspaceContext : ObservableObject, IProjectStateParticipant
{
    private Func<OccurrenceAnchor, IReadOnlyList<ResultsTokenViewModel>?>? _occurrenceContextProvider;
    private Func<OccurrenceAnchor, bool>? _occurrenceNavigator;
    private Func<OccurrenceAnchor, TextOccurrenceLocation?>? _occurrenceLocationProvider;

    public WorkspaceContext(
        SelectionViewModel selection, AssessViewModel assess, ChangesViewModel changes, ICommandClient commands, IHandoffFolderPicker folderPicker,
        IFileDragSource dragSource, BaselineViewModel baseline, TimeProvider? clock = null, IClipboard? clipboard = null,
        IDiagnosticFilePicker? diagnosticFiles = null, IDiagnosticWindowDialogs? diagnosticDialogs = null)
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
        Clipboard = clipboard ?? NoDesktopServices.Instance;
        DiagnosticFiles = diagnosticFiles ?? NoDesktopServices.Instance;
        DiagnosticDialogs = diagnosticDialogs ?? NoDesktopServices.Instance;
        Clock = clock ?? TimeProvider.System;
        Assess.PropertyChanged += OnAssessPropertyChanged;
        Assess.Words.Routes.OpenInText = OpenWord;
        Assess.Words.Routes.TryWord = TryWord;
        Evidence.PropertyChanged += OnEvidencePropertyChanged;
        _projectParticipants.Add(baseline);
        _projectParticipants.Add(selection);
        _projectParticipants.Add(Changes);
        _projectParticipants.Add(this);
        _projectParticipants.Add(new StoredEvidenceLoader(this));
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

    /// <summary>
    /// The shell's Refresh: captures a new Baseline from FieldWorks' last save, reloads every page, and settles an
    /// Apply whose result could not be confirmed.
    /// </summary>
    public IAsyncRelayCommand? RefreshProjectCommand { get; internal set; }

    /// <summary>The shell's action for measuring the saved Default Selection against the current Baseline.</summary>
    public IAsyncRelayCommand? ParseAllWordsCommand { get; internal set; }

    /// <summary>The shell's action for configuring the words to parse.</summary>
    public IRelayCommand? ConfigureCommand { get; internal set; }

    /// <summary>The sentence shared by every page when its measurements need a new parse.</summary>
    public string ParsePromptText => Assess.IsActive
        ? "Parsing… see the top row."
        : "Nothing parsed since the last Refresh.";

    /// <summary>The action offered by the shared parse prompt.</summary>
    public string ParsePromptActionText => Setup?.CanRunDefaultSelection == true
        ? "Parse all words"
        : "Choose what to parse";

    /// <summary>The command behind the shared parse prompt's action.</summary>
    public IRelayCommand? ParsePromptActionCommand => Setup?.CanRunDefaultSelection == true
        ? ParseAllWordsCommand
        : ConfigureCommand;

    /// <summary>Whether the shared prompt should offer an action while no Assessment is running.</summary>
    public bool ShowParsePromptAction => !Assess.IsActive;

    /// <summary>Where a page asks a person to choose a folder.</summary>
    public IHandoffFolderPicker FolderPicker { get; }

    /// <summary>How a page lets a person drag files out of the window.</summary>
    public IFileDragSource DragSource { get; }

    /// <summary>Where a page puts text a person copies; one composed without it fails each copy.</summary>
    public IClipboard Clipboard { get; }

    /// <summary>The dialogs a page opens and saves diagnostic JSON through; one composed without them fails each use.</summary>
    public IDiagnosticFilePicker DiagnosticFiles { get; }

    /// <summary>Gives a window that shows a saved diagnostic dialogs of its own; views call it, pages only pass it on.</summary>
    public IDiagnosticWindowDialogs DiagnosticDialogs { get; }

    /// <summary>The changes collected on any page and not applied yet; the Review changes page lists them.</summary>
    public ChangesViewModel Changes { get; }

    /// <summary>The command seam a page runs its own queries through.</summary>
    public ICommandClient Commands { get; }

    /// <summary>The clock every page reads: when a run completed, a check's elapsed time, what "today" is.</summary>
    public TimeProvider Clock { get; }

    /// <summary>The evidence every page shows, and whether its numbers are still current.</summary>
    public ProjectEvidence Evidence { get; } = new();

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

    partial void OnBaselineChanged(WorkspaceBaseline? value)
    {
        Evidence.Baseline = value;
        OnPropertyChanged(nameof(NeedsAssessment));
    }

    /// <summary>The grammar check in one line, as the page that owns the check last published it.</summary>
    [ObservableProperty]
    private GrammarSummary? _grammarSummary;

    /// <summary>Whether an Assessment is on screen for the open project.</summary>
    public bool HasEvidence => Evidence.HasAssessment;

    /// <summary>The negation of <see cref="HasEvidence"/>, so a view never composes <c>!</c> itself.</summary>
    public bool HasNoEvidence => !Evidence.HasAssessment;

    /// <summary>Whether the current Baseline has no Assessment and needs the saved Default Selection parsed.</summary>
    public bool NeedsAssessment => Baseline?.HasBaseline == true && !Evidence.HasAssessment;

    /// <summary>Whether the project and Selection controls accept input: not while an Assessment runs.</summary>
    public bool ProjectAndSelectionEnabled => !Assess.IsActive;

    /// <summary>The page the window is showing.</summary>
    [ObservableProperty]
    private WorkspacePage _currentPage;

    /// <summary>The object the inspector shows, or <see langword="null"/> while it is closed.</summary>
    [ObservableProperty]
    private OpenInspectorRequest? _inspector;

    private readonly List<PageModel> _pages = [];
    private readonly List<IProjectStateParticipant> _projectParticipants = [];
    private CancellationTokenSource? _openCancellation;
    private int _openGeneration;
    private bool _publishing;
    private bool _republish;

    /// <summary>The publication to the pages under way, or a completed task when none is.</summary>
    internal Task EvidencePublication { get; private set; } = Task.CompletedTask;

    /// <summary>Whether a project open is still running its stages, the stored evidence read among them.</summary>
    internal bool IsOpeningProject => Volatile.Read(ref _openCancellation) is not null;

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
        setup.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(SetupViewModel.CanRunDefaultSelection) or nameof(SetupViewModel.IsOpen)))
                return;
            OnPropertyChanged(nameof(ParsePromptActionText));
            OnPropertyChanged(nameof(ParsePromptActionCommand));
        };
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
        Baseline = null;
        Evidence.Clear();
        GrammarSummary = null;
        CurrentPage = WorkspacePage.Overview;
        Inspector = null;
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

    /// <summary>
    /// Reloads the current Draft, rechecks it when nonempty, then refreshes Baseline evidence and pages.
    /// </summary>
    public async Task PublishBaselineCapturedAsync(CancellationToken cancellationToken = default)
    {
        await Changes.ReloadAsync(cancellationToken).ConfigureAwait(true);
        if (Changes.Count > 0)
            await Changes.RecheckAsync(cancellationToken).ConfigureAwait(true);
        if (ProjectPath is { } projectPath)
            await LoadStoredEvidenceAsync(projectPath, cancellationToken).ConfigureAwait(true);
        foreach (var page in _pages.ToArray())
            await page.BaselineCapturedAsync(cancellationToken).ConfigureAwait(true);
        if (Setup is not null) await Setup.BaselineCapturedAsync().ConfigureAwait(true);
    }

    /// <summary>Stops whatever work any page has running, and returns once each has stopped.</summary>
    public async Task StopPageWorkAsync()
    {
        foreach (var page in _pages.ToArray()) await page.StopWorkAsync().ConfigureAwait(true);
    }

    /// <summary>Cancels and awaits project commands and page work before switching or closing the workspace.</summary>
    internal async Task StopProjectWorkAsync()
    {
        await Assess.DisposeAsync().ConfigureAwait(true);
        await Assess.Trace.StopAsync().ConfigureAwait(true);
        await StopPageWorkAsync().ConfigureAwait(true);
    }

    private bool IsCurrentOpen(int generation) => generation == Volatile.Read(ref _openGeneration);

    /// <summary>Publishes a completed in-session run as what every page now shows.</summary>
    public void PublishEvidence(WorkspaceEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Evidence.ShowRun(evidence);
        Changes.AssessmentId = Evidence.ParseTimeAssessmentId;
        _ = PublishToPagesAsync(CancellationToken.None);
    }

    /// <summary>Clears numbers from the replaced Baseline before pages reload its saved evidence.</summary>
    internal void ClearAssessmentForNewBaseline()
    {
        Evidence.ClearForNewBaseline();
        Changes.AssessmentId = null;
        _ = PublishToPagesAsync(CancellationToken.None);
    }

    /// <summary>Records that changes were applied to the FieldWorks project, so the numbers are stale.</summary>
    public void RecordApplied() => Evidence.AppliedSinceRefresh = true;

    /// <summary>
    /// Reads the open project's stored evidence again and publishes it, showing an Assessment recorded since, as
    /// when an agent ran one from the command line. It reads only; nothing is measured.
    /// </summary>
    public Task ReadStoredEvidenceAsync(CancellationToken cancellationToken = default) =>
        ProjectPath is { } projectPath ? LoadStoredEvidenceAsync(projectPath, cancellationToken) : Task.CompletedTask;

    // The stored read model's one way in; a run's rows stay on screen when the store holds the same run.
    internal Task PublishCurrentEvidenceAsync(
        CurrentEvidenceSnapshot evidence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var shown = Evidence.Assessment;
        Evidence.ShowStored(evidence);
        if (Evidence.Assessment is { IsStored: true } restored && !ReferenceEquals(restored, shown))
            Assess.Restore(restored);
        Changes.AssessmentId = Evidence.ParseTimeAssessmentId;
        return PublishToPagesAsync(cancellationToken);
    }

    private async Task LoadStoredEvidenceAsync(string projectPath, CancellationToken cancellationToken)
    {
        var generation = Volatile.Read(ref _openGeneration);
        var stored = await Commands.ReadCurrentEvidenceAsync(projectPath, cancellationToken).ConfigureAwait(true);
        if (!IsCurrentOpen(generation) || !string.Equals(projectPath, ProjectPath, StringComparison.Ordinal)) return;
        if (stored.Succeeded) await PublishCurrentEvidenceAsync(stored.Value!, cancellationToken).ConfigureAwait(true);
    }

    // Arrivals during a pass are folded into one more pass, so every page ends on the same, latest evidence.
    private Task PublishToPagesAsync(CancellationToken cancellationToken)
    {
        if (_publishing)
        {
            _republish = true;
            return EvidencePublication;
        }
        return EvidencePublication = RunPublicationAsync(cancellationToken);
    }

    private async Task RunPublicationAsync(CancellationToken cancellationToken)
    {
        _publishing = true;
        var generation = Volatile.Read(ref _openGeneration);
        try
        {
            do
            {
                _republish = false;
                await Task.WhenAll(_pages.ToArray()
                    .Select(page => page.EvidencePublishedAsync(Evidence, cancellationToken))).ConfigureAwait(true);
            }
            while (_republish && IsCurrentOpen(generation));
        }
        finally
        {
            _publishing = false;
            _republish = false;
        }
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
    /// <param name="tab">The tab to show.</param>
    public void OpenTexts(TextsTab tab) => OpenTexts(tab, null);

    /// <summary>Opens the Texts page on <paramref name="tab"/> with the requested matrix cells selected.</summary>
    /// <param name="tab">The tab to show.</param>
    /// <param name="cells">The matrix cells to select, or <see langword="null"/> to preserve the current selection.</param>
    public void OpenTexts(TextsTab tab, IReadOnlyList<TextsListCell>? cells) =>
        Open(new OpenTextsRequest(tab, cells));

    /// <summary>Opens <paramref name="word"/> in the Texts page's word list, with every filter cleared.</summary>
    public void OpenWord(string word) => Open(new OpenWordRequest(word));

    public void OpenOccurrence(OccurrenceAnchor? occurrence, string word, string? wordformId = null)
    {
        if (occurrence is { } anchor && _occurrenceNavigator?.Invoke(anchor) == true)
        {
            OpenTexts(TextsTab.AnalyzeTexts);
            return;
        }
        Open(new OpenWordRequest(word, wordformId));
    }

    /// <summary>Registers the Texts page as the source of loaded sentence context.</summary>
    internal void RegisterOccurrenceContextProvider(
        Func<OccurrenceAnchor, IReadOnlyList<ResultsTokenViewModel>?> provider) =>
        _occurrenceContextProvider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <summary>Gets the loaded sentence for an exact source occurrence, when it is available.</summary>
    internal IReadOnlyList<ResultsTokenViewModel>? OccurrenceContext(OccurrenceAnchor occurrence) =>
        _occurrenceContextProvider?.Invoke(occurrence);

    internal void RegisterOccurrenceNavigator(Func<OccurrenceAnchor, bool> navigator) =>
        _occurrenceNavigator = navigator ?? throw new ArgumentNullException(nameof(navigator));

    internal void RegisterOccurrenceLocationProvider(Func<OccurrenceAnchor, TextOccurrenceLocation?> provider) =>
        _occurrenceLocationProvider = provider ?? throw new ArgumentNullException(nameof(provider));

    internal TextOccurrenceLocation? OccurrenceLocation(OccurrenceAnchor occurrence) =>
        _occurrenceLocationProvider?.Invoke(occurrence);

    /// <summary>Opens Try a Word on <paramref name="word"/> and traces it straight away.</summary>
    public void TryWord(string word) => Open(new TryWordRequest(word));

    /// <summary>
    /// Opens the inspector on <paramref name="subject"/>, beside the page the window is showing, with its first
    /// breadcrumb naming <paramref name="from"/>, or the page when that is <see langword="null"/>.
    /// </summary>
    public void OpenInspector(InspectorSubject subject, string? from = null, InspectorTrace? trace = null,
        IReadOnlyList<InspectorDetail>? captured = null)
    {
        var request = new OpenInspectorRequest(subject ?? throw new ArgumentNullException(nameof(subject)))
        {
            From = from,
            Trace = trace,
            Captured = trace is null ? [] : captured ?? [],
        };
        // Asking again for the object already open starts its breadcrumb afresh, as a new request would.
        if (Equals(Inspector, request)) OnPropertyChanged(nameof(Inspector));
        else Inspector = request;
    }

    /// <summary>Closes the inspector.</summary>
    public void CloseInspector() => Inspector = null;

    /// <summary>Opens the Timing page on <paramref name="words"/>, filtered to <paramref name="rule"/> when named.</summary>
    public void OpenTiming(IReadOnlyList<string> words, TraceTimingKey? rule, string? label = null) => Open(new OpenTimingRequest(words, rule, label));

    /// <summary>Opens the AI Handoff page with <paramref name="words"/> as the words it will write.</summary>
    public void HandOff(IReadOnlyList<string> words) => Open(new HandOffRequest(words));

    public void HandOff(IReadOnlyList<string> words, WordTraceResponse? selectedTrace) =>
        Open(new HandOffRequest(words, selectedTrace));

    /// <summary>Opens the AI Handoff page with warning context for the chosen words.</summary>
    public void HandOffWarning(IReadOnlyList<string> words, WarningHandoffScope warningScope) =>
        Open(new HandOffRequest(words, WarningScope: warningScope));

    private void OnAssessPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AssessViewModel.IsActive)) return;
        OnPropertyChanged(nameof(ProjectAndSelectionEnabled));
        OnPropertyChanged(nameof(ParsePromptText));
        OnPropertyChanged(nameof(ShowParsePromptAction));
    }

    private void OnEvidencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectEvidence.HasAssessment))
        {
            OnPropertyChanged(nameof(HasEvidence));
            OnPropertyChanged(nameof(HasNoEvidence));
            OnPropertyChanged(nameof(NeedsAssessment));
        }
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

    // Opens beside the pages, once the Baseline is known, as a page's own read would.
    private sealed class StoredEvidenceLoader(WorkspaceContext context) : IProjectStateParticipant
    {
        public ProjectOpenStage OpenStage => ProjectOpenStage.Independent;

        public void ClearProject()
        {
        }

        public Task OpenProjectAsync(string projectPath, CancellationToken cancellationToken) =>
            context.LoadStoredEvidenceAsync(projectPath, cancellationToken);
    }
}
