using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Proposals page: every Proposal in a shared grouped table, with the selected Proposal shown beside it.
/// </summary>
public sealed partial class ProposalsPage : UserControl, IWorkspacePaneProvider
{
    /// <summary>Reserves a share of the page only while a section is shown.</summary>
    public static readonly IValueConverter VisibleRowHeight = new FuncValueConverter<bool, GridLength>(
        hasRequests => hasRequests ? GridLength.Star : GridLength.Auto);

    public ProposalsPage(ProposalsPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Page = page;
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        var table = this.FindControl<ListBox>("ProposalsRowsItems")!;
        DenseTable.Attach(table);
        // The binding drops the model's refusal raised inside the list's own write, so the list is put back afterwards.
        table.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (Page.SelectedProposal is { } kept && table.SelectedItem is ProposalCardViewModel chosen &&
                !ReferenceEquals(chosen, kept)) table.SelectedItem = kept;
        });
        _sidePanel = BuildPane("ProposalGroupFilterTemplate");
        _inspectorContent = BuildPane("ProposalInspectorTemplate");
        _inspectorActions = BuildPane("ProposalInspectorActionsTemplate");
    }

    /// <summary>The model that supplies this page's groups, filters and selected details.</summary>
    public ProposalsPageModel Page { get; }

    /// <summary>The title displayed over the group filters.</summary>
    public string SidePanelTitle => "Proposals";

    /// <summary>The page's group filters, attached by the workspace layout host.</summary>
    public Control SidePanel => _sidePanel;

    /// <summary>The selected Proposal's details and available actions.</summary>
    public Control InspectorContent => _inspectorContent;

    /// <summary>The selected Proposal's actions, pinned below the details so none is scrolled out of view.</summary>
    public Control InspectorActions => _inspectorActions;

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
    private readonly Control _inspectorActions;
}
