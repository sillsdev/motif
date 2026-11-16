using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Reads recorded grammar findings without starting a grammar check.</summary>
public sealed partial class WarningsDiagnosticsViewModel : ObservableObject
{
    private readonly WorkspaceContext _context;
    private int _generation;

    public WarningsDiagnosticsViewModel(WorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Grammar = new GrammarViewModel(context.Commands);
        context.Evidence.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProjectEvidence.IsStale) && context.Evidence.IsStale) Clear();
        };
    }

    /// <summary>Availability of the stored check, distinct from a measured empty check.</summary>
    [ObservableProperty]
    private string _statusText = MotifAppStrings.DiagnosticsUnavailable;

    /// <summary>The stored check and its attributed evidence.</summary>
    public GrammarViewModel Grammar { get; }

    /// <summary>Findings in the most recently read stored check.</summary>
    [ObservableProperty]
    private IReadOnlyList<GrammarWarning> _findings = [];

    /// <summary>The recorded findings formatted with the existing warning projection.</summary>
    public IReadOnlyList<GrammarWarningRowViewModel> Rows { get; private set; } = [];

    /// <summary>
    /// The number of findings the rows below add up to, by level. It is the same count the health strip gives,
    /// since both read the one stored check; the strip's word for it is "grammar warnings".
    /// </summary>
    public string SummaryText { get; private set; } = string.Empty;

    partial void OnFindingsChanged(IReadOnlyList<GrammarWarning> value)
    {
        SummaryText = value.Count == 0 ? string.Empty : string.Format(System.Globalization.CultureInfo.CurrentCulture,
            MotifAppStrings.DiagnosticsFindingsSummaryFormat,
            PluralText.Format(MotifAppStrings.WarningsFindingsPattern, value.Count),
            string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.WarningsBreakdownFormat,
                PluralText.Format(MotifAppStrings.WarningsErrorsPattern, value.Count(f => f.Severity == GrammarDiagnosticLevel.Error)),
                PluralText.Format(MotifAppStrings.WarningsWarningsPattern, value.Count(f => f.Severity == GrammarDiagnosticLevel.Warning)),
                PluralText.Format(MotifAppStrings.WarningsInformationPattern, value.Count(f => f.Severity == GrammarDiagnosticLevel.Information))));
        OnPropertyChanged(nameof(SummaryText));
        Rows = value.Select((finding, index) => (finding, index))
            .GroupBy(item => string.IsNullOrWhiteSpace(item.finding.Code)
                ? (Code: (string?)null, Index: item.index) : (Code: item.finding.Code, Index: -1))
            .Select(group => new GrammarWarningRowViewModel(group.Select(item => item.finding).ToArray(),
                new WordRowRoutes { OpenInText = _context.OpenWord })).ToArray();
        OnPropertyChanged(nameof(Rows));
    }

    internal void Clear()
    {
        _generation++;
        Findings = [];
        Grammar.Clear();
        StatusText = _context.Evidence.IsStale ? MotifAppStrings.DiagnosticsOutOfDate : MotifAppStrings.DiagnosticsUnavailable;
    }

    internal async Task ReadAsync(string projectPath, CancellationToken cancellationToken)
    {
        var generation = ++_generation;
        Findings = [];
        Grammar.Clear();
        StatusText = _context.Evidence.IsStale ? MotifAppStrings.DiagnosticsOutOfDate : MotifAppStrings.DiagnosticsUnavailable;
        if (_context.Evidence.IsStale) return;
        StatusText = MotifAppStrings.DiagnosticsReading;
        var baseline = _context.Baseline?.Token;
        SIL.Motif.Contract.Commands.CommandOutcome<SIL.Motif.Commands.Queries.StoredGrammarCheckResponse> result;
        try
        {
            result = await _context.Commands.ReadStoredGrammarCheckAsync(new GrammarCheckRequest(projectPath), cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Never leave "Reading" on screen: a cancelled or failed read says what it is.
            if (generation == _generation)
                StatusText = exception is OperationCanceledException ? MotifAppStrings.DiagnosticsUnavailable
                    : MotifAppStrings.DiagnosticsUnreadable;
            if (exception is OperationCanceledException) throw;
            System.Diagnostics.Trace.TraceError("Motif could not read the recorded grammar check: {0}", exception);
            return;
        }
        if (generation != _generation || cancellationToken.IsCancellationRequested || _context.ProjectPath != projectPath ||
            _context.Evidence.IsStale || _context.Baseline?.Token != baseline)
        {
            if (generation == _generation && StatusText == MotifAppStrings.DiagnosticsReading)
                StatusText = MotifAppStrings.DiagnosticsMismatched;
            return;
        }
        StatusText = result.Succeeded ? MotifAppStrings.DiagnosticsUnavailable : MotifAppStrings.DiagnosticsUnreadable;
        if (result.Succeeded && result.Value is { } stored)
        {
            Grammar.LoadStored(projectPath, stored.Check, stored.LastParserRefusal);
            if (stored.LastParserRefusal is { } refusal) StatusText = WindowRefusal.From(refusal).Sentence;
            else if (stored.Check is { HasBaseline: false }) StatusText = MotifAppStrings.GrammarBaselineRequired;
        }
        Findings = result.Succeeded && result.Value is { LastParserRefusal: null, Check: { HasBaseline: true } check }
            ? check.Findings : [];
        if (result.Succeeded && result.Value is { LastParserRefusal: null, Check.HasBaseline: true })
            StatusText = Findings.Count == 0 ? MotifAppStrings.DiagnosticsNoWarnings : MotifAppStrings.DiagnosticsIntro;
    }
}
