using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Reads stored next actions for Progress and facts for the shared health strip.</summary>
public sealed partial class ProgressPageModel : PageModel
{
    private int _generation;
    private CancellationTokenSource _stop = new();
    private CancellationTokenSource? _activeRead;
    private readonly List<Task> _reads = [];
    private IReadOnlyList<Guid> _readTexts = [];
    private string? _readProject;
    private string _readWords = string.Empty;

    public ProgressPageModel(WorkspaceContext context) : base(context)
    {
        TryAgainCommand = new AsyncRelayCommand(RefreshCurrentAsync, () => Context.HasProject && !IsReading);
        OpenTextsCommand = new RelayCommand(() => Context.OpenPage(WorkspacePage.Texts));
        OpenWordsCommand = new RelayCommand(() => Context.OpenPage(WorkspacePage.Words));
        Health.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HealthStripViewModel.Overview)) RaiseParseStateChanged();
        };
        context.Selection.PropertyChanged += OnSelectionChanged;
        WatchSetup();
        context.Evidence.PropertyChanged += OnEvidenceChanged;
        context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceContext.Setup)) WatchSetup();
            if (e.PropertyName == nameof(WorkspaceContext.HasProject)) TryAgainCommand.NotifyCanExecuteChanged();
            if (e.PropertyName == nameof(WorkspaceContext.CurrentPage) &&
                context.CurrentPage == WorkspacePage.Progress && !context.IsOpeningProject && !IsReadingForCurrentState())
                _ = RefreshCurrentAsync(CancellationToken.None);
        };
    }

    private SetupViewModel? _watchedSetup;

    private void WatchSetup()
    {
        if (ReferenceEquals(_watchedSetup, Context.Setup)) return;
        if (_watchedSetup is not null) _watchedSetup.PropertyChanged -= OnSetupChanged;
        _watchedSetup = Context.Setup;
        if (_watchedSetup is not null) _watchedSetup.PropertyChanged += OnSetupChanged;
        RaiseNothingParsedChanged();
    }

    private void OnSetupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SetupViewModel.CanRunDefaultSelection)) RaiseNothingParsedChanged();
    }

    /// <summary>The health facts shared with the shell.</summary>
    public HealthStripViewModel Health => Context.Health;

    /// <summary>The complete waiting count, or null when not read or unreadable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWaitingProposals), nameof(WaitingProposalsText))]
    private int? _waitingProposals;

    partial void OnWaitingProposalsChanged(int? value) => RebuildRows();

    public bool HasWaitingProposals => WaitingProposals > 0;

    public string WaitingProposalsText => WaitingProposals is { } count
        ? PluralText.Format(MotifAppStrings.ProgressWaitingProposalsPattern, count) : string.Empty;

    /// <summary>Only stored facts with a live destination become rows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextRows), nameof(HasNextSection))]
    private IReadOnlyList<ProgressNextRow> _nextRows = [];

    public bool HasNextRows => NextRows.Count > 0;
    public bool HasNextSection => HasNextRows || IsReading || HasUnavailableNext;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextSection))]
    private bool _isReading;

    partial void OnIsReadingChanged(bool value)
    {
        TryAgainCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowReadingParse));
    }

    /// <summary>A failed or partial read is distinct from a successful empty list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextSection))]
    private bool _hasUnavailableNext;

    public IAsyncRelayCommand TryAgainCommand { get; }

    /// <summary>Opens Texts, where the Texts to parse are chosen.</summary>
    public IRelayCommand OpenTextsCommand { get; }

    /// <summary>Opens Words, where parsed words are read.</summary>
    public IRelayCommand OpenWordsCommand { get; }

    /// <summary>The stored Overview when it describes the current project, else null.</summary>
    private OverviewResponse? CurrentOverview => !Context.Evidence.IsStale && Health.Overview is { IsStale: false } overview
        ? overview : null;

    /// <summary>Whether a current stored Overview is known, so "nothing parsed" is a fact and not a guess.</summary>
    public bool HasParseFacts => CurrentOverview is not null;

    /// <summary>Whether the project has a stored parse of the chosen Texts.</summary>
    public bool HasParse => CurrentOverview is { AssessmentId: not null };

    /// <summary>Whether the project is known to have no stored parse yet.</summary>
    public bool ShowNothingParsed => CurrentOverview is { AssessmentId: null };

    /// <summary>Whether the page can offer to parse what is already chosen, instead of asking for Texts to be chosen.</summary>
    public bool ShowParseChosen => ShowNothingParsed && Context.Setup?.CanRunDefaultSelection == true &&
        Context.ParseAllWordsCommand is not null;

    /// <summary>Whether nothing is parsed and nothing is chosen yet, so the page asks for Texts to be chosen.</summary>
    public bool ShowChooseTexts => ShowNothingParsed && !ShowParseChosen;

    /// <summary>Says how many Texts are chosen, so the page does not ask for a choice that has been made.</summary>
    public string NothingParsedChosenText => Context.Selection.ChosenTextIds.Count is var count and > 0
        ? PluralText.Format(MotifAppStrings.ProgressChosenTextsPattern, count)
        : MotifAppStrings.ProgressChosenWordsReady;

    /// <summary>The shell's Parse All Words command, which the page offers once something is chosen.</summary>
    public IAsyncRelayCommand? ParseAllWordsCommand => Context.ParseAllWordsCommand;

    private void RaiseNothingParsedChanged()
    {
        OnPropertyChanged(nameof(ShowParseChosen));
        OnPropertyChanged(nameof(ShowChooseTexts));
        OnPropertyChanged(nameof(NothingParsedChosenText));
        OnPropertyChanged(nameof(ParseAllWordsCommand));
    }

    /// <summary>Whether the stored parse facts are being read, so the page says so instead of standing blank.</summary>
    public bool ShowReadingParse => IsReading && !HasParseFacts && !OverviewUnavailable;

    /// <summary>The parse facts as sentences, each from a stored count; empty before a parse.</summary>
    public IReadOnlyList<string> ParseSummaryLines
    {
        get
        {
            if (CurrentOverview is not { AssessmentId: not null } overview) return [];
            var culture = CultureInfo.CurrentCulture;
            var coverage = overview.TextCoverage;
            List<string> lines = [];
            if (coverage.TotalOccurrences > 0)
                lines.Add(string.Format(culture, MotifAppStrings.ProgressParsedPlacesFormat,
                    coverage.ParsedOccurrences.ToString("N0", culture), coverage.TotalOccurrences.ToString("N0", culture)));
            lines.Add(string.Format(culture, MotifAppStrings.ProgressParsedWordsFormat,
                coverage.ParsedWords.ToString("N0", culture), coverage.NoParseWords.ToString("N0", culture),
                coverage.UnknownWords.ToString("N0", culture), coverage.SkippedWords.ToString("N0", culture)));
            if (coverage.NotMeasuredWords > 0)
                lines.Add(PluralText.Format(MotifAppStrings.ProgressNotMeasuredPattern, coverage.NotMeasuredWords));
            if (overview.AssessedUtc is { } assessed)
                lines.Add(string.Format(culture, MotifAppStrings.ProgressLastParsedFormat, assessed.ToLocalTime().ToString("g", culture)));
            return lines;
        }
    }

    /// <summary>The parse facts as one paragraph, a sentence per line.</summary>
    public string ParseSummaryText => string.Join(Environment.NewLine, ParseSummaryLines);

    /// <summary>Whether the stored Overview could not be read, so nothing may be claimed about parsing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOfferNextTryAgain), nameof(ShowReadingParse), nameof(ShowOverviewUnavailable))]
    private bool _overviewUnavailable;

    private void RaiseParseStateChanged()
    {
        OnPropertyChanged(nameof(HasParseFacts));
        OnPropertyChanged(nameof(HasParse));
        OnPropertyChanged(nameof(ShowNothingParsed));
        OnPropertyChanged(nameof(ParseSummaryLines));
        OnPropertyChanged(nameof(ParseSummaryText));
        OnPropertyChanged(nameof(ShowReadingParse));
        RaiseNothingParsedChanged();
    }

    /// <summary>The words under Next when a read was refused, naming the store problem when there is one.</summary>
    public string UnavailableNextText => _storeProblem is null ? MotifAppStrings.ProgressNextUnavailable
        : MotifAppStrings.ProgressNextStoreProblem;

    /// <summary>Whether the page says the parse results could not be read; not when the store itself is the cause and the window already says so.</summary>
    public bool ShowOverviewUnavailable => OverviewUnavailable && CanOfferTryAgain;

    /// <summary>False when the store itself cannot be used and asking again would only refuse again.</summary>
    public bool CanOfferTryAgain => _storeProblem is null;

    /// <summary>Whether the Next notice offers Try Again: only when the parse card does not already offer it.</summary>
    public bool CanOfferNextTryAgain => CanOfferTryAgain && !OverviewUnavailable;

    private WindowRefusal? _storeProblem;

    /// <summary>Whether the store problem is an older store that Refresh upgrades.</summary>
    public bool ShowRefreshToUpgrade => _storeProblem?.OffersRefreshToUpgrade == true && Context.RefreshProjectCommand is not null;

    /// <summary>The shell's Refresh, which upgrades an older store.</summary>
    public IAsyncRelayCommand? RefreshProjectCommand => Context.RefreshProjectCommand;

    private void RaiseStoreProblemChanged()
    {
        OnPropertyChanged(nameof(ShowRefreshToUpgrade));
        OnPropertyChanged(nameof(RefreshProjectCommand));
        OnPropertyChanged(nameof(UnavailableNextText));
        OnPropertyChanged(nameof(CanOfferTryAgain));
        OnPropertyChanged(nameof(CanOfferNextTryAgain));
        OnPropertyChanged(nameof(ShowOverviewUnavailable));
    }
    private Task? _earlyRead;
    private EvidenceSet? _earlySet;

    private void NoteUnavailable(Refusal? refusal)
    {
        HasUnavailableNext = true;
        if (refusal is null) return;
        var window = WindowRefusal.From(refusal);
        if (!window.IsStoreCauseThatRetryCannotClear) return;
        _storeProblem = window;
        RaiseStoreProblemChanged();
    }

    protected override void OnProjectCleared()
    {
        Invalidate();
        Health.ClearProject();
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        RefreshAsync(projectPath, cancellationToken);

    protected override Task OnBaselineCapturedAsync(CancellationToken cancellationToken) => RefreshHealthAsync(cancellationToken);

    protected override Task OnEvidencePublishedAsync(EvidenceLens lens, CancellationToken cancellationToken)
    {
        // The refresh started when the run completed is already reading (or has read) this evidence's Overview.
        if (_earlyRead is { } early && ReferenceEquals(_earlySet, Context.Evidence.Set) &&
            (!early.IsCompleted || Health.Overview is not null))
            return early;
        return RefreshHealthAsync(cancellationToken);
    }

    protected override Task OnGrammarCheckedAsync(CancellationToken cancellationToken) => RefreshHealthAsync(cancellationToken);

    private Task RefreshHealthAsync(CancellationToken cancellationToken)
    {
        Health.Overview = null;
        return RefreshCurrentAsync(cancellationToken);
    }

    private Task RefreshCurrentAsync(CancellationToken cancellationToken) => Context.ProjectPath is { } project
        ? RefreshAsync(project, cancellationToken) : Task.CompletedTask;

    protected override async Task OnStopWorkAsync()
    {
        Invalidate();
        _stop.Cancel();
        await Task.WhenAll(_reads).ConfigureAwait(true);
        _reads.Clear();
        _stop.Dispose();
        _stop = new CancellationTokenSource();
    }

    private void Invalidate()
    {
        ++_generation;
        var active = _activeRead;
        _activeRead = null;
        active?.Cancel();
        var previous = NextRows;
        WaitingProposals = null;
        NextRows = [];
        HasUnavailableNext = false;
        OverviewUnavailable = false;
        _storeProblem = null;
        RaiseStoreProblemChanged();
        IsReading = false;
        foreach (var row in previous) row.OpenCommand.NotifyCanExecuteChanged();
    }

    private Task RefreshAsync(string project, CancellationToken cancellationToken)
    {
        Invalidate();
        _readProject = project;
        _readTexts = Context.Selection.ChosenTextIds.ToArray();
        _readWords = Context.Selection.PastedWords;
        HasUnavailableNext = Context.Evidence.IsStale;
        _reads.RemoveAll(task => task.IsCompletedSuccessfully);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        _activeRead = linked;
        IsReading = true;
        var read = ReadAsync(project, _generation, linked);
        _reads.Add(read);
        return read;
    }

    private async Task ReadAsync(string project, int generation, CancellationTokenSource linked)
    {
        try
        {
            await Task.WhenAll(ReadOverviewAsync(project, generation, linked.Token),
                ReadStandingsAsync(project, generation, linked.Token)).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally
        {
            if (generation == _generation && linked.IsCancellationRequested) Invalidate();
            if (generation == _generation) IsReading = false;
            if (ReferenceEquals(_activeRead, linked)) _activeRead = null;
            linked.Dispose();
        }
    }

    /// <summary>Whether a running read started for the state now shown.</summary>
    private bool IsReadingForCurrentState() => IsReading && _activeRead is not null && _readProject == Context.ProjectPath &&
        !Context.Evidence.IsStale && SelectionMatches();

    private bool SelectionMatches() => Context.Selection.ChosenTextIds.SequenceEqual(_readTexts) &&
        Context.Selection.PastedWords == _readWords;

    private bool IsCurrent(string? project, int generation) => generation == _generation &&
        Context.ProjectPath == project && SelectionMatches();

    private async Task ReadOverviewAsync(string project, int generation, CancellationToken cancellationToken)
    {
        var result = await Context.Commands.OverviewAsync(new OverviewRequest(project), cancellationToken).ConfigureAwait(true);
        if (!IsCurrent(project, generation) || cancellationToken.IsCancellationRequested) return;
        if (result.Value is { IsStale: true })
        {
            Invalidate();
            HasUnavailableNext = true;
        }
        Health.Overview = result.Succeeded ? result.Value : null;
        OverviewUnavailable = !result.Succeeded;
        if (!result.Succeeded && result.Refusal is { } refusal && WindowRefusal.From(refusal) is
            { IsStoreCauseThatRetryCannotClear: true } storeCause)
        {
            _storeProblem = storeCause;
            RaiseStoreProblemChanged();
        }
        RaiseParseStateChanged();
    }

    private async Task ReadStandingsAsync(string project, int generation, CancellationToken cancellationToken)
    {
        var result = await Context.Commands.ProposalStandingsAsync(new ProposalStandingsRequest(project), cancellationToken)
            .ConfigureAwait(true);
        if (!IsCurrent(project, generation) || cancellationToken.IsCancellationRequested || Context.Evidence.IsStale) return;
        if (result.Succeeded && result.Value is { Unreadable.Count: 0 } value)
            WaitingProposals = value.Proposals.Count(ProposalsPageModel.IsWaiting);
        else NoteUnavailable(result.Refusal);
    }

    private void RebuildRows()
    {
        var project = Context.ProjectPath;
        var generation = _generation;
        IRelayCommand OpenProposals() => new RelayCommand(() =>
        {
            if (IsCurrent(project, generation) && !Context.Evidence.IsStale) Context.OpenPage(WorkspacePage.Proposals);
        }, () => IsCurrent(project, generation) && !Context.Evidence.IsStale);
        List<ProgressNextRow> rows = [];
        if (HasWaitingProposals)
            rows.Add(new(MotifAppStrings.BoardProposals, WaitingProposalsText, AutomationIds.ProgressNextProposals, OpenProposals()));
        NextRows = rows;
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SelectionViewModel.ChosenTextIds) or nameof(SelectionViewModel.PastedWords))
            RaiseNothingParsedChanged();
        if (e.PropertyName is nameof(SelectionViewModel.ChosenTextIds) or nameof(SelectionViewModel.PastedWords) &&
            !SelectionMatches()) Invalidate();
    }

    private void OnEvidenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectEvidence.IsStale)) RaiseParseStateChanged();
        if (e.PropertyName is nameof(ProjectEvidence.Baseline) or nameof(ProjectEvidence.Set) ||
            e.PropertyName == nameof(ProjectEvidence.IsStale) && Context.Evidence.IsStale) Invalidate();
        // A run finished in this window: read its parse facts now, not after every page has reloaded its words.
        if (e.PropertyName == nameof(ProjectEvidence.Set) && Context.Evidence is { Set: { } set, Assessment: not null, IsStale: false } &&
            !Context.IsOpeningProject && Context.ProjectPath is not null)
        {
            _earlySet = set;
            _earlyRead = RefreshCurrentAsync(CancellationToken.None);
        }
    }
}
