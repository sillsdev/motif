using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>A project-independent window for reviewing one saved diagnostic document.</summary>
public sealed partial class DiagnosticWindow : Window
{
    private DiagnosticPanel? _panel;

    public DiagnosticWindow(TraceWordViewModel trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        AvaloniaXamlLoader.Load(this);
        _panel = new DiagnosticPanel(trace);
        this.FindControl<ContentControl>("PanelHost")!.Content = _panel;
    }

    public void ShowDiagnosticError(string message) => _panel?.ShowError(message);
}
