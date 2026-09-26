using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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
        this.FindControl<ContentControl>("RichDiagnosticHost")!.Content =
            new DiagnosticPanel(model.Diagnostics, showResultSummary: false);
        model.SavedDiagnosticOpened += OnSavedDiagnosticOpened;
    }

    public TraceWordViewModel Trace { get; }

    public TryWordPageModel Model { get; }

    private async void OnOpenDiagnosticClick(object? sender, RoutedEventArgs e) =>
        await Model.OpenSavedDiagnosticAsync();

    private void OnSavedDiagnosticOpened(DiagnosticToolsViewModel opened)
    {
        if (TopLevel.GetTopLevel(this) is Window owner) new DiagnosticWindow(opened).Show(owner);
    }
}
