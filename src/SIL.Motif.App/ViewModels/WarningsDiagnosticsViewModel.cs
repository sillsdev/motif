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
        context.Evidence.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProjectEvidence.IsStale) && context.Evidence.IsStale) Clear();
        };
    }

    /// <summary>Findings in the most recently read stored check.</summary>
    [ObservableProperty]
    private IReadOnlyList<GrammarWarning> _findings = [];

    /// <summary>The recorded findings formatted with the existing warning projection.</summary>
    public IReadOnlyList<GrammarWarningRowViewModel> Rows { get; private set; } = [];

    partial void OnFindingsChanged(IReadOnlyList<GrammarWarning> value)
    {
        Rows = value.Select(finding => new GrammarWarningRowViewModel(finding)).ToArray();
        OnPropertyChanged(nameof(Rows));
    }

    internal void Clear()
    {
        _generation++;
        Findings = [];
    }

    internal async Task ReadAsync(string projectPath, CancellationToken cancellationToken)
    {
        var generation = ++_generation;
        Findings = [];
        if (_context.Evidence.IsStale) return;
        var baseline = _context.Baseline?.Token;
        var result = await _context.Commands.ReadStoredGrammarCheckAsync(new GrammarCheckRequest(projectPath), cancellationToken)
            .ConfigureAwait(true);
        if (generation != _generation || cancellationToken.IsCancellationRequested || _context.ProjectPath != projectPath ||
            _context.Evidence.IsStale || _context.Baseline?.Token != baseline) return;
        Findings = result.Succeeded && result.Value is { LastParserRefusal: null, Check: { HasBaseline: true } check }
            ? check.Findings : [];
    }
}
