using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>A project-independent window for reviewing one saved diagnostic document.</summary>
public sealed partial class DiagnosticWindow : Window
{
    /// <summary>Shows <paramref name="opened"/> with tools whose open and save dialogs belong to this window.</summary>
    /// <param name="opened">The saved diagnostic, or why it could not be shown.</param>
    /// <param name="opener">The tools it was opened from, whose clipboard the new tools share.</param>
    public DiagnosticWindow(OpenedDiagnostic opened, DiagnosticToolsViewModel opener)
    {
        ArgumentNullException.ThrowIfNull(opened);
        ArgumentNullException.ThrowIfNull(opener);
        AvaloniaXamlLoader.Load(this);
        var tools = opener.ForOpened(opened, opener.WindowDialogs.For(this));
        this.FindControl<ContentControl>("PanelHost")!.Content = new DiagnosticPanel(tools);
    }
}
