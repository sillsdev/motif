using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Handoff strip and its file list, bound to its own <see cref="Handoff"/> view model.</summary>
public sealed partial class HandoffPanel : UserControl
{
    public HandoffPanel(HandoffViewModel handoff)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        Handoff = handoff;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public HandoffViewModel Handoff { get; }

    // The routed PointerPressed gesture is what a native drag session actually starts from.
    private async void OnFileTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button) return;
        if ((sender as Control)?.DataContext is HandoffFileViewModel file)
            await Handoff.DragFileAsync(e, file);
    }

    private async void OnAllFilesPointerPressed(object? sender, PointerPressedEventArgs e) =>
        await Handoff.DragAllFilesAsync(e);

    private async void OnCopyPathClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not HandoffFileViewModel file) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is { } clipboard) await clipboard.SetTextAsync(file.FullPath);
    }

    private async void OnCopyStarterPromptClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(HandoffViewModel.StarterPromptMarkdown);
    }
}
