using System.ComponentModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Where a Why This Analysis fold stands.</summary>
public enum WhyThisAnalysisState
{
    /// <summary>Folded: nothing has been asked of the parser.</summary>
    Collapsed,
    /// <summary>Expanded and waiting on the parser's trace.</summary>
    Loading,
    /// <summary>A trace for exactly this word and Baseline is shown.</summary>
    Ready,
    /// <summary>The parser or the command refused the trace.</summary>
    Refused,
    /// <summary>A trace came back but is not for this word or this Baseline, so none is shown.</summary>
    Mismatched,
    /// <summary>No trace can be read: no project, or nothing was recorded.</summary>
    Unavailable,
}

/// <summary>One name a trace recorded, with where it opens in FieldWorks when that is verified.</summary>
/// <param name="Name">The name as FieldWorks shows it.</param>
/// <param name="Detail">A short recorded fact beside the name, or empty.</param>
/// <param name="Link">The verified FieldWorks address, or null when there is none.</param>
public sealed record WhyNameRow(string Name, string Detail, Uri? Link)
{
    public bool HasLink => Link is not null;
    public bool HasDetail => Detail.Length > 0;
    public string LinkName => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.WhyThisAnalysisOpenNameFormat, Name);
}

/// <summary>One analysis the parser built, as the names it is made of.</summary>
/// <param name="Title">The morphs of the analysis written in order.</param>
/// <param name="Parts">Each morph by name with its FieldWorks link.</param>
public sealed record WhyAnalysisRow(string Title, IReadOnlyList<WhyNameRow> Parts);

/// <summary>One attempt the parser tried and abandoned.</summary>
/// <param name="Text">What the attempt had built.</param>
/// <param name="Stopped">What ended it, naming the rule when one did.</param>
public sealed record WhyFailedRow(string Text, string Stopped);

/// <summary>One recorded parser event of the Full Trace.</summary>
/// <param name="Kind">The event's plain kind.</param>
/// <param name="Name">The rule or entry it names, or empty.</param>
/// <param name="Outcome">What the parser recorded happened.</param>
/// <param name="Address">The event's address in the recorded tree.</param>
/// <param name="Link">The verified FieldWorks address, or null.</param>
public sealed record WhyTraceRow(string Kind, string Name, string Outcome, string Address, Uri? Link)
{
    public bool HasLink => Link is not null;
    public bool HasName => Name.Length > 0;
    public string LinkName => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.WhyThisAnalysisOpenNameFormat, Name);
}

/// <summary>
/// The Why This Analysis fold of one word. Folded, it does no work. Expanded, it asks the parser for this word's
/// trace once, against the Baseline in use, and shows only a trace whose word and Baseline match. Closing it,
/// turning Diagnostics off, or changing the project or Baseline cancels the request and ignores any late answer.
/// </summary>
public sealed partial class WhyThisAnalysisViewModel : ObservableObject, IDisposable
{
    /// <summary>Failed attempts listed in the fold itself; the Full Trace holds every recorded event.</summary>
    public const int FailedRowLimit = 20;

    private readonly WorkspaceContext _context;
    private readonly TraceWordViewModel _trace;
    private Task _running = Task.CompletedTask;
    private int _generation;
    private bool _disposed;

    /// <param name="context">The window's project, Baseline and Diagnostics switch.</param>
    /// <param name="form">The word as written, exactly as the panel shows it.</param>
    public WhyThisAnalysisViewModel(WorkspaceContext context, string form)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(form);
        _context = context;
        Form = form;
        _trace = new TraceWordViewModel(context.Commands, null, context.Clock) { TryWordSessions = context.TryWordSessions };
        _trace.PropertyChanged += OnTraceChanged;
        context.PropertyChanged += OnContextChanged;
        context.Evidence.PropertyChanged += OnEvidenceChanged;
        CopyTraceCommand = new AsyncRelayCommand(CopyTraceAsync, () => State == WhyThisAnalysisState.Ready && _trace.HasDiagnosticJson);
        OpenInFieldWorksCommand = new AsyncRelayCommand<Uri>(OpenAsync, uri => uri is not null && _context.UriLauncher is not null);
        ToggleFullTraceCommand = new RelayCommand(() => ShowFullTrace = !ShowFullTrace, () => State == WhyThisAnalysisState.Ready);
    }

    /// <summary>The word this fold asks about.</summary>
    public string Form { get; }

    /// <summary>The Baseline the request was made against, or null when none was known.</summary>
    public BaselineToken? RequestedBaseline { get; private set; }

    /// <summary>The trace this fold runs; it belongs to this fold alone, so no other word's result can reach it.</summary>
    internal TraceWordViewModel Trace => _trace;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    private WhyThisAnalysisState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _showFullTrace;

    public bool IsLoading => State == WhyThisAnalysisState.Loading;
    public bool IsReady => State == WhyThisAnalysisState.Ready;
    public bool HasStatus => StatusText.Length > 0 && State != WhyThisAnalysisState.Collapsed;

    public IAsyncRelayCommand CopyTraceCommand { get; }
    public IAsyncRelayCommand<Uri> OpenInFieldWorksCommand { get; }
    public IRelayCommand ToggleFullTraceCommand { get; }

    public IReadOnlyList<WhyAnalysisRow> Analyses { get; private set; } = [];
    public IReadOnlyList<WhyNameRow> Rules { get; private set; } = [];
    public IReadOnlyList<WhyFailedRow> FailedRows { get; private set; } = [];
    public IReadOnlyList<WhyTraceRow> TraceRows { get; private set; } = [];

    /// <summary>The tool automation id of the fold, unique to its word.</summary>
    public string AutomationId => AutomationIds.WhyThisAnalysisFold;

    /// <summary>What the heading says beside its name: that Diagnostics is on and the real parser work, in steps.</summary>
    public string Subtitle
    {
        get
        {
            if (State != WhyThisAnalysisState.Ready || _trace.Result is not { } result)
                return MotifAppStrings.BoardDiagnostics;
            return result.TotalParserWork is { } steps
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.WhyThisAnalysisStepsFormat,
                    PluralText.Format(MotifAppStrings.WhyThisAnalysisStepsPattern, steps))
                : MotifAppStrings.WhyThisAnalysisStepsNotRecorded;
        }
    }

    /// <summary>Whether the parser built any analysis.</summary>
    public bool HasAnalyses => Analyses.Count > 0;
    public bool HasNoAnalyses => IsReady && Analyses.Count == 0;
    public bool HasRules => Rules.Count > 0;
    public bool HasFailedRows => FailedRows.Count > 0;

    /// <summary>The heading over the failed attempts. It states a count only for what was really recorded.</summary>
    public string FailedHeading { get; private set; } = string.Empty;

    /// <summary>
    /// A sentence when the failed attempts cannot be taken as the whole story: the search stopped early, or the
    /// trace recorded none. Empty when the search completed and the attempts shown are all of them.
    /// </summary>
    public string FailedNote { get; private set; } = string.Empty;
    public bool HasFailedNote => FailedNote.Length > 0;

    /// <summary>The count of failed attempts beyond those listed.</summary>
    public string MoreFailedText { get; private set; } = string.Empty;
    public bool HasMoreFailed => MoreFailedText.Length > 0;

    public string FullTraceCountText => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.WhyThisAnalysisFullTraceCountFormat, TraceRows.Count);

    public string FullTraceToggleText => ShowFullTrace ? MotifAppStrings.WhyThisAnalysisHideFullTrace : MotifAppStrings.WhyThisAnalysisFullTrace;

    partial void OnShowFullTraceChanged(bool value) => OnPropertyChanged(nameof(FullTraceToggleText));

    partial void OnIsExpandedChanged(bool value)
    {
        if (_disposed) return;
        if (value) Begin();
        else Cancel(clearRows: true);
    }

    partial void OnStateChanged(WhyThisAnalysisState value)
    {
        CopyTraceCommand.NotifyCanExecuteChanged();
        ToggleFullTraceCommand.NotifyCanExecuteChanged();
    }

    private BaselineToken? CurrentBaseline() =>
        _context.Baseline?.Token ?? _context.Evidence.Stored?.Baseline?.Token ??
        _context.Evidence.Assessment?.Assessment.Baseline.Token;

    private void Begin()
    {
        if (!_context.Diagnostics) { Cancel(clearRows: true); return; }
        var generation = ++_generation;
        ClearRows();
        if (_context.ProjectPath is not { } path)
        {
            Settle(WhyThisAnalysisState.Unavailable, MotifAppStrings.WhyThisAnalysisNoProject);
            return;
        }
        RequestedBaseline = CurrentBaseline();
        _trace.Reset();
        Settle(WhyThisAnalysisState.Loading, MotifAppStrings.WhyThisAnalysisLoading);
        _running = RunAsync(generation, path, RequestedBaseline, _running);
    }

    private async Task RunAsync(int generation, string path, BaselineToken? requested, Task? previous)
    {
        try
        {
            // The earlier request was cancelled; its command must finish unwinding before this one can start.
            if (previous is not null) await AwaitPreviousAsync(previous).ConfigureAwait(true);
            if (_disposed || generation != _generation) return;
            _trace.Reset();
            _trace.SetProjectPath(path);
            _trace.SetWord(Form);
            await _trace.TryCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Observed here so the faulted task is never left unobserved for the next run or StopAsync.
            System.Diagnostics.Trace.TraceError("Motif's trace request for '{0}' failed: {1}", Form, exception);
            if (!_disposed && generation == _generation && IsExpanded)
                Settle(WhyThisAnalysisState.Unavailable, MotifAppStrings.WhyThisAnalysisNotRecorded);
            return;
        }
        if (_disposed || generation != _generation || !IsExpanded || !_context.Diagnostics ||
            !string.Equals(path, _context.ProjectPath, StringComparison.Ordinal)) return;
        Receive(requested);
    }

    // A failed earlier run must not fault this one: its own request is already superseded.
    private static async Task AwaitPreviousAsync(Task previous)
    {
        try
        {
            await previous.ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceError("Motif's earlier trace request failed: {0}", exception);
        }
    }

    private void Receive(BaselineToken? requested)
    {
        if (_trace.Refusal is { } refusal)
        {
            var reason = WindowRefusal.From(refusal).Sentence;
            Settle(WhyThisAnalysisState.Refused,
                string.Format(CultureInfo.CurrentCulture, MotifAppStrings.WhyThisAnalysisRefusedFormat, reason));
            return;
        }
        if (_trace.Result is not { } result)
        {
            Settle(WhyThisAnalysisState.Unavailable, MotifAppStrings.WhyThisAnalysisNotRecorded);
            return;
        }
        if (!SameWord(result.Word, Form))
        {
            _trace.Reset();
            Settle(WhyThisAnalysisState.Mismatched, MotifAppStrings.WhyThisAnalysisWrongWord);
            return;
        }
        var traced = result.HostCapture?.Baseline?.Token;
        var current = CurrentBaseline();
        if (requested is not null && traced is not null && !traced.HasSameSemanticIdentity(requested) ||
            current is not null && requested is not null && !current.HasSameSemanticIdentity(requested))
        {
            _trace.Reset();
            Settle(WhyThisAnalysisState.Mismatched, MotifAppStrings.WhyThisAnalysisMismatch);
            return;
        }
        BuildRows(result);
        Settle(WhyThisAnalysisState.Ready, string.Empty);
        OnPropertyChanged(nameof(Subtitle));
    }

    private static bool SameWord(string left, string right) => string.Equals(
        left.Trim().Normalize(NormalizationForm.FormD), right.Trim().Normalize(NormalizationForm.FormD),
        StringComparison.Ordinal);

    private void Settle(WhyThisAnalysisState state, string status)
    {
        StatusText = status;
        State = state;
        OnPropertyChanged(nameof(Subtitle));
    }

    private void BuildRows(WordTraceResponse result)
    {
        Analyses = _trace.Analyses.Select(analysis => new WhyAnalysisRow(
            string.Join(" ", analysis.Morphs.Select(morph => morph.Form)),
            analysis.Morphs.Select(morph => new WhyNameRow(
                morph.Form, morph.Category, morph.Link)).ToArray())).ToArray();
        var refs = (result.Reading.Refs ?? []).ToDictionary(reference => reference.Id, StringComparer.Ordinal);
        Rules = result.Reading.RulesOnBestPath.Select(rule =>
        {
            TraceRef? reference = null;
            if (rule.RefId is { } id) refs.TryGetValue(id, out reference);
            return new WhyNameRow(RuleName(rule, reference), rule.Outcome,
                reference?.FieldWorks is { Link: { } link } && Uri.TryCreate(link, UriKind.Absolute, out var uri) &&
                string.Equals(uri.Scheme, "silfw", StringComparison.OrdinalIgnoreCase) ? uri : null);
        }).ToArray();
        var failed = _trace.Candidates.Where(candidate => candidate.IsFailure).ToArray();
        FailedRows = failed.Take(FailedRowLimit).Select(candidate => new WhyFailedRow(candidate.Text, candidate.StopHeadline)).ToArray();
        MoreFailedText = failed.Length > FailedRowLimit
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.WhyThisAnalysisMoreFailedFormat, failed.Length - FailedRowLimit)
            : string.Empty;
        var stoppedEarly = result.SearchCompletion != TraceSearchCompletion.Complete;
        FailedHeading = stoppedEarly
            ? failed.Length > 0
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.WhyThisAnalysisFailedAtLeastFormat, failed.Length)
                : MotifAppStrings.WhyThisAnalysisFailedUnknown
            : failed.Length > 0
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.WhyThisAnalysisFailedFormat, failed.Length)
                : MotifAppStrings.WhyThisAnalysisFailedNoneRecorded;
        FailedNote = stoppedEarly
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.WhyThisAnalysisCappedFormat,
                string.IsNullOrWhiteSpace(result.StopReason) ? MotifAppStrings.TraceReasonNotRecorded : result.StopReason)
            : _trace.Candidates.Count == 0 && !_trace.HasStopGroups
                ? MotifAppStrings.WhyThisAnalysisAttemptsNotRecorded
                : string.Empty;
        TraceRows = _trace.ExpertEvents.Select(step => new WhyTraceRow(step.KindText, TraceName(step),
            step.RecordedOutcomeText, step.RecordedStep.StepId, step.FieldWorksLink)).ToArray();
        NotifyRows();
    }

    // The project's own rule name when captured; a raw parser id is only a fallback, labelled as a rule.
    internal static string RuleName(TraceRuleReading rule, TraceRef? reference)
    {
        if (reference?.CapturedFieldWorksLabel is { Length: > 0 } captured) return captured;
        var recorded = !string.IsNullOrWhiteSpace(reference?.Label) ? reference!.Label : rule.Rule;
        return RawRuleId.IsMatch(recorded)
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.WhyThisAnalysisRuleFormat, recorded) : recorded;
    }

    private static string TraceName(TraceStepViewModel step)
    {
        if (step.Reference?.CapturedFieldWorksLabel is { Length: > 0 } captured) return captured;
        return step.Source is { Length: > 0 } source && RawRuleId.IsMatch(source)
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.WhyThisAnalysisRuleFormat, source)
            : step.Source ?? string.Empty;
    }

    private static readonly System.Text.RegularExpressions.Regex RawRuleId = new(
        @"^[A-Za-z_]+#\d+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private void ClearRows()
    {
        Analyses = [];
        Rules = [];
        FailedRows = [];
        TraceRows = [];
        FailedHeading = FailedNote = MoreFailedText = string.Empty;
        ShowFullTrace = false;
        NotifyRows();
    }

    private void NotifyRows()
    {
        OnPropertyChanged(nameof(Analyses));
        OnPropertyChanged(nameof(HasAnalyses));
        OnPropertyChanged(nameof(HasNoAnalyses));
        OnPropertyChanged(nameof(Rules));
        OnPropertyChanged(nameof(HasRules));
        OnPropertyChanged(nameof(FailedRows));
        OnPropertyChanged(nameof(HasFailedRows));
        OnPropertyChanged(nameof(FailedHeading));
        OnPropertyChanged(nameof(FailedNote));
        OnPropertyChanged(nameof(HasFailedNote));
        OnPropertyChanged(nameof(MoreFailedText));
        OnPropertyChanged(nameof(HasMoreFailed));
        OnPropertyChanged(nameof(TraceRows));
        OnPropertyChanged(nameof(FullTraceCountText));
    }

    /// <summary>Stops the request, drops what it showed and returns to the folded state.</summary>
    private void Cancel(bool clearRows)
    {
        _generation++;
        _trace.Reset();
        if (clearRows) ClearRows();
        RequestedBaseline = null;
        Settle(WhyThisAnalysisState.Collapsed, string.Empty);
    }

    /// <summary>Folds the panel and drops everything it held, for a project, Baseline or Diagnostics change.</summary>
    public void Reset()
    {
        if (_disposed) return;
        Cancel(clearRows: true);
        if (IsExpanded) IsExpanded = false;
    }

    private void OnTraceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TraceWordViewModel.HasDiagnosticJson)) CopyTraceCommand.NotifyCanExecuteChanged();
    }

    private void OnContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceContext.Diagnostics) && !_context.Diagnostics ||
            e.PropertyName is nameof(WorkspaceContext.ProjectPath) or nameof(WorkspaceContext.Baseline)) Reset();
    }

    private void OnEvidenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectEvidence.Baseline) ||
            e.PropertyName == nameof(ProjectEvidence.IsStale) && _context.Evidence.IsStale) Reset();
    }

    private async Task CopyTraceAsync()
    {
        try { await _context.Clipboard.SetTextAsync(_trace.DiagnosticJson).ConfigureAwait(true); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusText = MotifAppStrings.WhyThisAnalysisCopyFailed;
        }
    }

    private async Task OpenAsync(Uri? uri)
    {
        if (uri is null || _context.UriLauncher is not { } launcher) return;
        try { await launcher.LaunchAsync(uri).ConfigureAwait(true); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusText = MotifAppStrings.WhyThisAnalysisOpenFailed;
        }
    }

    /// <summary>Cancels the request and returns once it has ended, before the project's files are released.</summary>
    public async Task StopAsync()
    {
        Reset();
        try
        {
            await _trace.StopAsync().ConfigureAwait(true);
            await _running.ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Cancels the request, ends every subscription and ignores any late answer.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        Cancel(clearRows: true);
        _disposed = true;
        _context.PropertyChanged -= OnContextChanged;
        _context.Evidence.PropertyChanged -= OnEvidenceChanged;
        _trace.PropertyChanged -= OnTraceChanged;
    }
}
