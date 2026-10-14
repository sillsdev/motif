using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Proposals page: every Proposal in a shared grouped table, with the selected Proposal shown beside it.
/// </summary>
public sealed partial class ProposalsPage : UserControl, IWorkspacePaneProvider
{
    public ProposalsPage(ProposalsPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Page = page;
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        DenseTable.Attach(this.FindControl<ListBox>("ProposalsRowsItems")!);
        _sidePanel = BuildPane("ProposalGroupFilterTemplate");
        _inspectorContent = BuildPane("ProposalInspectorTemplate");
    }

    /// <summary>The model that supplies this page's groups, filters and selected details.</summary>
    public ProposalsPageModel Page { get; }

    /// <summary>The title displayed over the group filters.</summary>
    public string SidePanelTitle => "Proposals";

    /// <summary>The page's group filters, attached by the workspace layout host.</summary>
    public Control SidePanel => _sidePanel;

    /// <summary>The selected Proposal's details and available actions.</summary>
    public Control InspectorContent => _inspectorContent;

    private Control BuildPane(string key)
    {
        var pane = ((IDataTemplate)Resources[key]!).Build(Page)!;
        pane.DataContext = Page;
        return pane;
    }

    private void OnRequestPageInspector(object? sender, RoutedEventArgs e) =>
        WorkspaceLayoutHost.RequestPageInspector(this);

    private readonly Control _sidePanel;
    private readonly Control _inspectorContent;
}
