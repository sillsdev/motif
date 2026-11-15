using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views.Assistant;

/// <summary>The Claude Requests and setup panel.</summary>
public sealed partial class ClaudePanel : UserControl
{
    public ClaudePanel(ClaudePanelViewModel model)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = model;
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? SetupRequested;
    private void OnClose(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnSetup(object? sender, RoutedEventArgs e) => SetupRequested?.Invoke(this, EventArgs.Empty);
}
