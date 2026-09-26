using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Try a Word page: a box for any word, and the trace of it against the current Baseline's grammar. Also
/// opens a diagnostic someone saved earlier in a window of its own.
/// </summary>
public sealed partial class TryWordPanel : UserControl
{
    public TryWordPanel(TryWordPageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
        Trace = model.Trace;
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ContentControl>("RichDiagnosticHost")!.Content = new DiagnosticPanel(Trace, showResultSummary: false);
    }

    public TraceWordViewModel Trace { get; }

    public TryWordPageModel Model { get; }

    private async void OnOpenDiagnosticClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        await SavedDiagnosticOpener.OpenFromPickerAsync(
            owner,
            trace => new DiagnosticWindow(trace).Show(owner),
            refusal =>
            {
                var window = new DiagnosticWindow(new TraceWordViewModel());
                window.Show(owner);
                window.ShowDiagnosticError(refusal);
            });
    }
}
