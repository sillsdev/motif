using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Handoff stage and its file list, bound to its own <see cref="Handoff"/> view model.</summary>
public sealed partial class HandoffPanel : UserControl
{
    public HandoffPanel(HandoffViewModel handoff)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        Handoff = handoff;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);

        // A Button handles its own pointer press, so the drag has to start ahead of it in the tunnel.
        this.FindControl<Button>("AllFilesButton")!
            .AddHandler(PointerPressedEvent, OnAllFilesPointerPressed, RoutingStrategies.Tunnel);
    }

    public HandoffViewModel Handoff { get; }

    /// <summary>What the folder will hold, shown before the first write so its contents are no surprise.</summary>
    public IReadOnlyList<PlannedHandoffFile> PlannedFiles { get; } =
        [.. HandoffFileViewModel.KnownFiles.Select(file => new PlannedHandoffFile(file.Name, file.Purpose))];

    /// <summary>Questions worth asking once the files are in a chat, as a starting point.</summary>
    public IReadOnlyList<string> Questions { get; } =
    [
        "Which words did not parse, and what do they have in common?",
        "Which words took longest, and what in the grammar might slow them?",
        "Which analyses the project stores did the parser not produce?",
    ];

    // The routed PointerPressed gesture is what a native drag session actually starts from.
    private async void OnFileTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button) return;
        if ((sender as Control)?.DataContext is HandoffFileViewModel file)
            await Handoff.DragFileAsync(e, file);
    }

    // Handled, so the press only drags: a keyboard activation of the same button is what copies the folder.
    private async void OnAllFilesPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        await Handoff.DragAllFilesAsync(e);
    }

    // A drag needs a pointer, so the keyboard route to the same files is their folder's path.
    private async void OnAllFilesClick(object? sender, RoutedEventArgs e)
    {
        if (Handoff.OutputDirectory is not { } folder) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is { } clipboard) await clipboard.SetTextAsync(folder);
    }

    private async void OnCopyPathClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not HandoffFileViewModel file) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is { } clipboard) await clipboard.SetTextAsync(file.FullPath);
    }

    private async void OnCopyQuestionClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string question) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is { } clipboard) await clipboard.SetTextAsync(question);
    }

    private async void OnCopyStarterPromptClick(object? sender, RoutedEventArgs e)
    {
        if (Handoff.PastedHeader is not { } header) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(header);
    }
}
