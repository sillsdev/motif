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

    partial void OnFindingsChanged(IReadOnlyList<GrammarWarning> value)
    {
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
        var result = await _context.Commands.ReadStoredGrammarCheckAsync(new GrammarCheckRequest(projectPath), cancellationToken)
            .ConfigureAwait(true);
        if (generation != _generation || cancellationToken.IsCancellationRequested || _context.ProjectPath != projectPath ||
            _context.Evidence.IsStale || _context.Baseline?.Token != baseline) return;
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
