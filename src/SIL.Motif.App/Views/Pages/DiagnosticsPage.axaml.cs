using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>Recorded warnings and timing grouped by rule.</summary>
public sealed partial class DiagnosticsPage : UserControl
{
    /// <summary>Creates the page over the workspace's shared page model.</summary>
    public DiagnosticsPage(WorkspaceContext context) : this(
        (context ?? throw new ArgumentNullException(nameof(context))).GetOrCreatePage(static owner => new DiagnosticsPageModel(owner))) { }

    public DiagnosticsPage(DiagnosticsPageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
    }
}
