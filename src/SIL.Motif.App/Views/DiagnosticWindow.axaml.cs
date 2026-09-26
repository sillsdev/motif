using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>A project-independent window for reviewing one saved diagnostic document.</summary>
public sealed partial class DiagnosticWindow : Window
{
    public DiagnosticWindow(DiagnosticToolsViewModel tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ContentControl>("PanelHost")!.Content = new DiagnosticPanel(tools);
    }
}
