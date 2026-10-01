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
            new DiagnosticPanel(model.Diagnostics, showResultSummary: false, showAnalyses: false);
        model.SavedDiagnosticOpened += OnSavedDiagnosticOpened;
    }

    public TraceWordViewModel Trace { get; }

    public TryWordPageModel Model { get; }

    private async void OnOpenDiagnosticClick(object? sender, RoutedEventArgs e) =>
        await Model.OpenSavedDiagnosticAsync();

    private async void OnCopyInstructionsClick(object? sender, RoutedEventArgs e) =>
        await Model.Diagnostics.CopyInstructionsAsync();

    private async void OnSaveClick(object? sender, RoutedEventArgs e) => await Model.Diagnostics.SaveAsync();

    private async void OnCopyJsonClick(object? sender, RoutedEventArgs e) => await Model.Diagnostics.CopyJsonAsync();

    private void OnSavedDiagnosticOpened(OpenedDiagnostic opened)
    {
        if (TopLevel.GetTopLevel(this) is Window owner) new DiagnosticWindow(opened, Model.Diagnostics).Show(owner);
    }
}
