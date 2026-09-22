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

    public DiagnosticPanel(TraceWordViewModel trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        DataContext = trace;
        AvaloniaXamlLoader.Load(this);
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
            detailGrid.RowDefinitions = new RowDefinitions("Auto,12,Auto");
            Grid.SetColumn(tree, 0);
            Grid.SetRow(tree, 0);
            Grid.SetColumn(details, 0);
            Grid.SetRow(details, 2);
        }
        else
        {
            detailGrid.ColumnDefinitions = new ColumnDefinitions("2*,12,1*");
            detailGrid.RowDefinitions = new RowDefinitions("*");
            Grid.SetColumn(tree, 0);
            Grid.SetRow(tree, 0);
            Grid.SetColumn(details, 2);
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
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open diagnostic JSON",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Motif diagnostic JSON") { Patterns = ["*.json"] }],
            });
            if (files.Count == 0) return;

            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            var loaded = TraceWordViewModel.FromDiagnosticJson(await reader.ReadToEndAsync());
            new DiagnosticWindow(loaded).Show();
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }
}
