using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Reads stored timing rows grouped by rule for the current Assessment.</summary>
public sealed partial class SpeedByRuleViewModel : ObservableObject
{
    private readonly WorkspaceContext _context;
    private int _generation;
    private CancellationTokenSource? _linkStop;
    private readonly List<Task> _linkReads = [];
    internal Task LinkRead { get; private set; } = Task.CompletedTask;

    public SpeedByRuleViewModel(WorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        context.Evidence.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProjectEvidence.ParseTimeAssessmentId) or
                nameof(ProjectEvidence.TimingOverrideAssessmentIds) ||
                e.PropertyName == nameof(ProjectEvidence.IsStale) && context.Evidence.IsStale) Clear();
        };
    }

    /// <summary>Timing rows from the recorded Assessment.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    private IReadOnlyList<TimingWorkAggregate> _rows = [];

    /// <summary>Whether any recorded rule rows are available.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>Rule work divided only by the words that recorded work.</summary>
    [ObservableProperty]
    private IReadOnlyList<DiagnosticRuleRow> _displayRows = [];

    /// <summary>The exact denominator and requested scope of the stored work.</summary>
    [ObservableProperty]
    private string _denominatorText = string.Empty;

    /// <summary>Why per-word detail is incomplete, without discarding complete aggregates.</summary>
    [ObservableProperty]
    private string _detailText = string.Empty;

    /// <summary>Why no recorded rule work can be shown.</summary>
    [ObservableProperty]
    private string _unavailableText = MotifAppStrings.DiagnosticsNoWork;

    /// <summary>The denominator supplied by the recorded work summary.</summary>
    [ObservableProperty]
    private int _measuredWordCount;

    /// <summary>The selected recorded work row.</summary>
    [ObservableProperty]
    private TimingWorkAggregate? _selectedWork;

    partial void OnSelectedWorkChanged(TimingWorkAggregate? value) =>
        SelectedRow = value is null ? null : new DiagnosticRuleRow(value, MeasuredWordCount);

    /// <summary>The selected rule, resolved by exact recorded identity.</summary>
    [ObservableProperty]
    private DiagnosticRuleRow? _selectedRow;

    /// <summary>A verified FieldWorks destination for the selected rule, never a rule definition.</summary>
    [ObservableProperty]
    private Uri? _fieldWorksAddress;

    partial void OnSelectedRowChanged(DiagnosticRuleRow? value)
    {
        _linkStop?.Cancel();
        _linkStop?.Dispose();
        _linkStop = null;
        FieldWorksAddress = null;
        if (!_context.Diagnostics || value is null || value.Work.IsUnattributed ||
            _context.ProjectPath is not { } path) return;
        var stop = _linkStop = new CancellationTokenSource();
        _linkReads.RemoveAll(task => task.IsCompletedSuccessfully);
        LinkRead = ResolveLinkAsync(path, value, _generation, stop.Token);
        _linkReads.Add(LinkRead);
    }

    private async Task ResolveLinkAsync(string path, DiagnosticRuleRow row, int generation, CancellationToken token)
    {
        try
        {
            var baseline = _context.Baseline?.Token;
            var subject = InspectorSubject.Rule(row.Work.TimingKey, row.Name, row.Work.IdentityQuality);
            var outcome = await _context.Commands.InspectAsync(new InspectRequest(path, subject), token).ConfigureAwait(true);
            if (token.IsCancellationRequested || generation != _generation || SelectedRow != row ||
                _context.ProjectPath != path || _context.Evidence.IsStale || _context.Baseline?.Token != baseline) return;
            if (outcome.Value is { IsStale: false, Resolution: InspectorResolution.Resolved, Subject: { } returned,
                Facts: { Status: InspectorSectionStatus.Available, Value: { } facts } } &&
                returned.TimingKey == subject.TimingKey && outcome.Succeeded)
            {
                var target = facts.Rule?.FieldWorks ?? facts.Entry?.FieldWorks;
                if (Uri.TryCreate(target?.Link, UriKind.Absolute, out var uri) && uri.Scheme == "silfw")
                    FieldWorksAddress = uri;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    internal async Task StopLinksAsync()
    {
        Clear();
        await Task.WhenAll(_linkReads).ConfigureAwait(true);
        _linkReads.Clear();
    }

    internal void Clear()
    {
        _generation++;
        SelectedWork = null;
        SelectedRow = null;
        MeasuredWordCount = 0;
        _linkStop?.Cancel();
        FieldWorksAddress = null;
        Rows = [];
        DisplayRows = [];
        DenominatorText = DetailText = string.Empty;
        UnavailableText = _context.Evidence.IsStale ? MotifAppStrings.DiagnosticsOutOfDate : MotifAppStrings.DiagnosticsNoWork;
    }

    internal async Task ReadAsync(string projectPath, CancellationToken cancellationToken)
    {
        Clear();
        var generation = _generation;
        var assessmentId = _context.Evidence.ParseTimeAssessmentId;
        if (assessmentId is null || _context.Evidence.IsStale)
        {
            Rows = [];
            return;
        }
        UnavailableText = MotifAppStrings.DiagnosticsReading;
        var baseline = _context.Baseline?.Token;
        var overrides = _context.Evidence.TimingOverrideAssessmentIds.ToArray();
        var result = await _context.Commands.TimingAsync(new TimingRequest(projectPath, assessmentId, By: "rule",
            OverrideAssessmentIds: overrides),
            cancellationToken).ConfigureAwait(true);
        if (generation != _generation || cancellationToken.IsCancellationRequested || _context.ProjectPath != projectPath ||
            assessmentId != _context.Evidence.ParseTimeAssessmentId || baseline != _context.Baseline?.Token ||
            !overrides.SequenceEqual(_context.Evidence.TimingOverrideAssessmentIds)) return;
        UnavailableText = !result.Succeeded ? MotifAppStrings.DiagnosticsUnreadable
            : result.Value is { IsStale: true } or { CurrentProjectIsStale: true } ? MotifAppStrings.DiagnosticsOutOfDate
            : result.Value is { } recorded && (recorded.AssessmentId != assessmentId || recorded.By != "rule")
                ? MotifAppStrings.DiagnosticsMismatched : MotifAppStrings.DiagnosticsNoWork;
        Rows = result.Succeeded && result.Value is { IsStale: false, CurrentProjectIsStale: false,
            ObjectTimingAggregatesAvailable: true } timing && timing.AssessmentId == assessmentId && timing.By == "rule" &&
            (baseline is null || timing.Baseline is null || timing.Baseline == baseline)
            ? timing.ParserWorkAggregates : [];
        if (Rows.Count == 0 || result.Value is not { } measured) return;
        var count = MeasuredWordCount = measured.Work.MeasuredWordCount;
        DenominatorText = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.DiagnosticsMeasuredFormat,
            count, measured.WordCount);
        DetailText = measured.ObjectTimingDetail is { IsComplete: false }
            ? MotifAppStrings.DiagnosticsPartial : string.Empty;
        DisplayRows = Rows.Select(row => new DiagnosticRuleRow(row, count)).ToArray();
        UnavailableText = string.Empty;
    }
}

/// <summary>A recorded rule total with its exact measured-word denominator.</summary>
public sealed record DiagnosticRuleRow(TimingWorkAggregate Work, int MeasuredWords)
{
    public string Name => Work.IsUnattributed ? MotifAppStrings.TimingNotAttributedLabel : Work.Name;
    public string StepsPerWord => MeasuredWords > 0
        ? ((decimal)Work.ParserWork / MeasuredWords).ToString("N2", CultureInfo.CurrentCulture)
        : MotifAppStrings.TimingNotRecorded;
}
