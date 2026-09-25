using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>A reusable rich diagnostic presentation that can be hosted in Results or a standalone window.</summary>
public sealed partial class DiagnosticPanel : UserControl
{
    private const string FormatGuide =
        "https://github.com/sillsdev/motif/blob/main/docs/handoff/trace-diagnostic-format.md";

    /// <summary>Controls whether the panel shows its standalone result summary.</summary>
    public static readonly StyledProperty<bool> ShowResultSummaryProperty =
        AvaloniaProperty.Register<DiagnosticPanel, bool>(nameof(ShowResultSummary), defaultValue: true);

    public DiagnosticPanel(TraceWordViewModel trace)
        : this(trace, showResultSummary: true)
    {
    }

    /// <summary>Builds the diagnostic view with its result summary optionally hidden for a page with its own summary.</summary>
    /// <param name="trace">The trace and recorded diagnostic details to display.</param>
    /// <param name="showResultSummary">Whether this panel displays the trace's result summary.</param>
    public DiagnosticPanel(TraceWordViewModel trace, bool showResultSummary)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ShowResultSummary = showResultSummary;
        DataContext = trace;
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Whether the panel shows the trace summary above its diagnostic details.</summary>
    public bool ShowResultSummary
    {
        get => GetValue(ShowResultSummaryProperty);
        set => SetValue(ShowResultSummaryProperty, value);
    }

    /// <summary>Opens a saved diagnostic and sends its result or error to the caller.</summary>
    /// <param name="owner">The top-level window that owns the file picker.</param>
    /// <param name="showDiagnostic">Displays the diagnostic after it has been read and validated.</param>
    /// <param name="showError">Displays an error encountered while choosing, reading, or parsing the file.</param>
    public static Task OpenSavedDiagnosticAsync(
        TopLevel owner,
        Action<TraceWordViewModel> showDiagnostic,
        Action<string> showError)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(showDiagnostic);
        ArgumentNullException.ThrowIfNull(showError);

        return OpenSavedDiagnosticAsync(async () =>
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open diagnostic JSON",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Motif diagnostic JSON") { Patterns = ["*.json"] }],
            });
            if (files.Count == 0) return null;

            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }, showDiagnostic, showError);
    }

    private static async Task OpenSavedDiagnosticAsync(
        Func<Task<string?>> readDiagnostic,
        Action<TraceWordViewModel> showDiagnostic,
        Action<string> showError)
    {
        try
        {
            var json = await readDiagnostic();
            if (json is null) return;
            showDiagnostic(TraceWordViewModel.FromDiagnosticJson(json));
        }
        catch (Exception exception)
        {
            showError(exception.Message);
        }
    }

    private TraceWordViewModel Trace => (TraceWordViewModel)DataContext!;

    private void OnPanelSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var detailGrid = this.FindControl<Grid>("DetailGrid");
        var tree = this.FindControl<TreeView>("TreeHost");
        var details = this.FindControl<Border>("DetailHost");
        if (detailGrid is null || tree is null || details is null) return;

        if (e.NewSize.Width < 760)
        {
            detailGrid.ColumnDefinitions = new ColumnDefinitions("1*");
            detailGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            Grid.SetColumn(tree, 0);
            Grid.SetRow(tree, 0);
            Grid.SetColumn(details, 0);
            Grid.SetRow(details, 1);
        }
        else
        {
            detailGrid.ColumnDefinitions = new ColumnDefinitions("2*,1*");
            detailGrid.RowDefinitions = new RowDefinitions("*");
            Grid.SetColumn(tree, 0);
            Grid.SetRow(tree, 0);
            Grid.SetColumn(details, 1);
            Grid.SetRow(details, 0);
        }
    }


    private async void OnCopyJsonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(Trace.DiagnosticJson);
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private async void OnCopyInstructionsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                var text = $"Interpret this saved Motif diagnostic as recorded evidence. Read analyses before attempts, keep parser order, " +
                           $"treat incomplete search as incomplete even with a success, and label category counts aggregate rather than " +
                           $"step timing. Format guide: {FormatGuide}";
                await clipboard.SetTextAsync(text);
            }
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    internal void ShowError(string message)
    {
        if (this.FindControl<TextBlock>("ErrorText") is { } error)
        {
            error.Text = $"Diagnostic file error: {message}";
            error.IsVisible = true;
        }
    }

    private async void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;
            var files = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save diagnostic JSON",
                SuggestedFileName = $"{Trace.Result?.Word ?? "diagnostic"}.trace.json",
                FileTypeChoices = [new FilePickerFileType("Motif diagnostic JSON") { Patterns = ["*.json"] }],
            });
            if (files is not { } saved) return;
            await using var stream = await saved.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(Trace.DiagnosticJson);
            await writer.FlushAsync();
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private async void OnOpenClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } owner) return;
        await OpenSavedDiagnosticAsync(owner, trace => new DiagnosticWindow(trace).Show(), ShowError);
    }
}
