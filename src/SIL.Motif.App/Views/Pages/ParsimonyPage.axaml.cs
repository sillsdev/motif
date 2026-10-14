using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Parsimony page: the findings of the latest stored Parsimony Report, grouped by measure, with the evidence for
/// the finding selected. It only reads what is stored.
/// </summary>
public sealed partial class ParsimonyPage : UserControl, IWorkspacePaneProvider
{
    private readonly Control _sidePanel;
    private readonly Control _inspectorContent;

    public ParsimonyPage(ParsimonyPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        _sidePanel = BuildPane("ParsimonySidePanelTemplate", page);
        _inspectorContent = BuildPane("ParsimonyInspectorTemplate", page);
        DenseTable.Attach(this.FindControl<ListBox>("ParsimonyRowsItems")!);
    }

    public string SidePanelTitle => "Parsimony";

    public Control SidePanel => _sidePanel;

    public Control InspectorContent => _inspectorContent;

    private void OnAsk(object? sender, RoutedEventArgs e)
    {
        CloseDenseActionMenus();
        WorkspaceLayoutHost.RequestPageInspector(this);
    }

    private void OnShowDetails(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ParsimonyListRow { Link: { } link } row } &&
            DataContext is ParsimonyPageModel page && !ReferenceEquals(page.SelectedTableEntry, row))
            row.SelectCommand?.Execute(link);
        CloseDenseActionMenus();
        WorkspaceLayoutHost.RequestPageInspector(this);
    }

    private void OnSuppressedDetails(object? sender, RoutedEventArgs e) =>
        WorkspaceLayoutHost.RequestPageInspector(this);

    private void CloseDenseActionMenus()
    {
        var rows = this.FindControl<ListBox>("ParsimonyRowsItems");
        if (rows is null) return;
        foreach (var menu in rows.GetVisualDescendants().OfType<Button>()
                     .Where(button => button.Classes.Contains("denseTableMenu")))
            if (menu.Flyout is Flyout flyout) flyout.Hide();
    }

    private Control BuildPane(string key, ParsimonyPageModel page)
    {
        var pane = ((IDataTemplate)Resources[key]!).Build(page)!;
        pane.DataContext = page;
        return pane;
    }
}
