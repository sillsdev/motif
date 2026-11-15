using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;

namespace SIL.Motif.App.Views;

/// <summary>Shows Proposal standings and the selected Proposal's stored discussion.</summary>
public sealed partial class ProposalsPage : UserControl, IWorkspacePaneProvider
{
    /// <summary>Creates the page over the workspace's shared page model.</summary>
    public ProposalsPage(WorkspaceContext context) : this(
        (context ?? throw new ArgumentNullException(nameof(context))).GetOrCreatePage(static owner => new ProposalsPageModel(owner))) { }

    public ProposalsPage(ProposalsPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Page = page;
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        SidePanel = ((IDataTemplate)Resources["ProposalListPane"]!).Build(page)!;
        SidePanel.DataContext = page;
        var list = SidePanel.GetLogicalDescendants().OfType<ListBox>()
            .Single(control => control.Name == "ProposalsRowsItems");
        list.AddHandler(InputElement.PointerPressedEvent, (_, args) =>
        {
            if (!args.GetCurrentPoint(list).Properties.IsLeftButtonPressed || args.Source is not Visual source ||
                source.GetSelfAndVisualAncestors().OfType<CheckBox>().Any() ||
                source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext
                    is not ProposalCardViewModel card) return;
            if (!SelectRow(card)) args.Handled = true;
        }, RoutingStrategies.Tunnel);
        DenseTable.Attach(list, row => SelectRow(row), SelectRow);
        list.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (Page.SelectedProposal is { } kept && list.SelectedItem is ProposalCardViewModel chosen &&
                !ReferenceEquals(chosen, kept)) list.SelectedItem = kept;
        });
    }

    private void OnComparisonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: ProposalCardViewModel card } check)
            check.SetCurrentValue(CheckBox.IsCheckedProperty, card.IsCheckedForComparison);
    }

    /// <summary>The page's Proposal state.</summary>
    public ProposalsPageModel Page { get; }

    /// <summary>The title of the Proposal list.</summary>
    public string SidePanelTitle => MotifAppStrings.BoardOpenProposals;

    /// <summary>The page-owned Proposal list.</summary>
    public Control SidePanel { get; }

    private bool SelectRow(object row)
    {
        if (row is not ProposalCardViewModel card) return false;
        Page.SelectProposalCommand.Execute(card);
        return ReferenceEquals(Page.SelectedProposal, card);
    }
}
