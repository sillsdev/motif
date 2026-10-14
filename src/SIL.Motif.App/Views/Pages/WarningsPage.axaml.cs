using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Warnings page: the grammar's findings as the grammar panel shows them, with an offer to check the
/// grammar when nothing has been checked for this Baseline yet.
/// </summary>
public sealed partial class WarningsPage : UserControl, IWorkspacePaneProvider
{
    private readonly Control _sidePanel;
    private readonly Control _inspectorContent;

    public WarningsPage(WarningsPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ContentControl>("GrammarHost")!.Content = new GrammarPanel(page);
        _sidePanel = new WarningsFilterPanel(page);
        _inspectorContent = new WarningsInspectorPanel(page);
    }

    /// <summary>The Warnings page's side-panel title.</summary>
    public string SidePanelTitle => "Warnings";

    /// <summary>The Warnings area and report filters.</summary>
    public Control? SidePanel => _sidePanel;

    /// <summary>The selected finding and its evidence.</summary>
    public Control? InspectorContent => _inspectorContent;

    /// <summary>Warnings has no action pinned below the Inspector.</summary>
    public Control? InspectorFooter => null;
}
