using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Try a Word page: a box for any word, and the trace of it against the current Baseline's grammar. Also
/// opens a diagnostic someone saved earlier in a window of its own.
/// </summary>
public sealed partial class TryWordPanel : UserControl
{
    public TryWordPanel(TraceWordViewModel trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        Trace = trace;
        DataContext = trace;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ContentControl>("RichDiagnosticHost")!.Content = new DiagnosticPanel(trace);
    }

    public TraceWordViewModel Trace { get; }

    private async void OnOpenDiagnosticClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        try
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open diagnostic JSON",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Motif diagnostic JSON") { Patterns = ["*.json"] }],
            });
            if (files.Count == 0) return;

            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            var trace = TraceWordViewModel.FromDiagnosticJson(await reader.ReadToEndAsync());
            new DiagnosticWindow(trace).Show(owner);
        }
        catch (Exception exception)
        {
            var window = new DiagnosticWindow(new TraceWordViewModel());
            window.Show(owner);
            window.ShowDiagnosticError($"Unable to open diagnostic: {exception.Message}");
        }
    }
}
