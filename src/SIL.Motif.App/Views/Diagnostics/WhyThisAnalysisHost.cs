using System.ComponentModel;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views.Diagnostics;

/// <summary>
/// Fills the word panel's diagnostic slot with a Why This Analysis fold, only while Diagnostics is on. With
/// Diagnostics off nothing is built, so the word panel is exactly what it was. A new word panel gets a new fold.
/// </summary>
public sealed class WhyThisAnalysisHost : IDisposable
{
    private readonly WorkspaceContext _context;
    private readonly ResultsInTextViewModel _inText;
    private WordOccurrencesViewModel? _panel;
    private WhyThisAnalysisViewModel? _fold;

    public WhyThisAnalysisHost(WorkspaceContext context, ResultsInTextViewModel inText)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(inText);
        _context = context;
        _inText = inText;
        _inText.PropertyChanged += OnTextChanged;
        _context.PropertyChanged += OnContextChanged;
        Refresh();
    }

    /// <summary>The fold now in the slot, or null when Diagnostics is off or no word panel is open.</summary>
    public WhyThisAnalysisViewModel? Fold => _fold;

    private void OnTextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsInTextViewModel.OccurrencePanel)) Refresh();
    }

    private void OnContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.Diagnostics)) Refresh();
    }

    private void Refresh()
    {
        var panel = _inText.OccurrencePanel;
        if (ReferenceEquals(panel, _panel) && (_fold is not null) == (_context.Diagnostics && panel is not null)) return;
        Release();
        _panel = panel;
        if (panel is null || !_context.Diagnostics) return;
        _fold = new WhyThisAnalysisViewModel(_context, panel.Form);
        panel.DiagnosticContent = new WhyThisAnalysisPanel(_fold);
    }

    private void Release()
    {
        if (_panel is not null) _panel.DiagnosticContent = null;
        _fold?.Dispose();
        _fold = null;
        _panel = null;
    }

    /// <summary>Cancels the open fold's request and returns once it has ended.</summary>
    public async Task StopAsync()
    {
        if (_fold is { } fold) await fold.StopAsync().ConfigureAwait(true);
    }

    public void Dispose()
    {
        _inText.PropertyChanged -= OnTextChanged;
        _context.PropertyChanged -= OnContextChanged;
        Release();
    }
}
