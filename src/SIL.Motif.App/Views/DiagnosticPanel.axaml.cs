using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>A reusable rich diagnostic presentation that can be hosted in Results or a standalone window.</summary>
public sealed partial class DiagnosticPanel : UserControl
{
    /// <summary>Controls whether the panel shows its standalone result summary.</summary>
    public static readonly StyledProperty<bool> ShowResultSummaryProperty =
        AvaloniaProperty.Register<DiagnosticPanel, bool>(nameof(ShowResultSummary), defaultValue: true);

    public static readonly StyledProperty<bool> ShowFullDerivationProperty =
        AvaloniaProperty.Register<DiagnosticPanel, bool>(nameof(ShowFullDerivation), defaultValue: true);

    public bool ShowFullDerivation
    {
        get => GetValue(ShowFullDerivationProperty);
        set => SetValue(ShowFullDerivationProperty, value);
    }

    public static readonly StyledProperty<bool> ShowAttemptSummariesProperty =
        AvaloniaProperty.Register<DiagnosticPanel, bool>(nameof(ShowAttemptSummaries), defaultValue: true);

    public bool ShowAttemptSummaries
    {
        get => GetValue(ShowAttemptSummariesProperty);
        set => SetValue(ShowAttemptSummariesProperty, value);
    }

    public DiagnosticPanel(DiagnosticToolsViewModel tools)
        : this(tools, showResultSummary: true)
    {
    }

    /// <summary>
    /// Builds the diagnostic view with its result summary and analyses optionally left to a page that shows its own.
    /// </summary>
    /// <param name="tools">The trace to display, with the tools that copy, save, and open diagnostics beside it.</param>
    /// <param name="showResultSummary">Whether this panel displays the trace's result summary.</param>
    /// <param name="showAnalyses">Whether this panel displays the trace's analyses, ahead of its attempts.</param>
    /// <param name="showAttemptSummaries">Whether the panel displays shared summaries of terminal attempts.</param>
    public DiagnosticPanel(DiagnosticToolsViewModel tools, bool showResultSummary, bool showAnalyses = true,
        bool showAttemptSummaries = true)
    {
        ArgumentNullException.ThrowIfNull(tools);
        Tools = tools;
        ShowResultSummary = showResultSummary;
        ShowAttemptSummaries = showAttemptSummaries;
        DataContext = tools.Trace;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TreeView>("TreeHost")!.AddHandler(InputElement.KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
        if (showAnalyses) this.FindControl<ContentControl>("AnalysesHost")!.Content = new TraceAnalysesView();
        // Inside Try a Word the page already holds the inset and the answer, so the trace starts flush.
        if (!showResultSummary) Classes.Add("embedded");
        tools.DiagnosticOpened += opened => new DiagnosticWindow(opened, tools).Show();
    }

    /// <summary>The tools beside the trace, which also say why the last of them failed.</summary>
    public DiagnosticToolsViewModel Tools { get; }

    /// <summary>Whether the panel shows the trace summary above its diagnostic details.</summary>
    public bool ShowResultSummary
    {
        get => GetValue(ShowResultSummaryProperty);
        set => SetValue(ShowResultSummaryProperty, value);
    }

    private void OnPanelSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var detailGrid = this.FindControl<Grid>("DetailGrid");
        var tree = this.FindControl<TreeView>("TreeHost");
        var details = this.FindControl<Border>("DetailHost");
        if (detailGrid is null || tree is null || details is null) return;

        if (e.NewSize.Width < 760)
        {
            detailGrid.ColumnDefinitions = new ColumnDefinitions("1*");
            detailGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            Grid.SetColumn(tree, 0);
            Grid.SetRow(tree, 0);
            Grid.SetColumn(details, 0);
            Grid.SetRow(details, 1);
        }
        else
        {
            detailGrid.ColumnDefinitions = new ColumnDefinitions("2*,1*");
            detailGrid.RowDefinitions = new RowDefinitions("*");
            Grid.SetColumn(tree, 0);
            Grid.SetRow(tree, 0);
            Grid.SetColumn(details, 1);
            Grid.SetRow(details, 0);
        }
    }

    private async void OnCopyJsonClick(object? sender, RoutedEventArgs e) => await Tools.CopyJsonAsync();

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Down or Key.Up) ||
            sender is not TreeView tree || e.Source is not Control source) return;
        var current = source as TreeViewItem ?? source.GetLogicalAncestors().OfType<TreeViewItem>().FirstOrDefault();
        if (current is null) return;
        TreeViewItem? target = null;
        if (e.Key == Key.Down && current.IsExpanded && current.ItemCount > 0)
            target = Realize(current, 0);
        else
        {
            var cursor = current;
            while (cursor.GetLogicalAncestors().OfType<ItemsControl>().FirstOrDefault() is { } owner)
            {
                var index = owner.IndexFromContainer(cursor);
                if (e.Key == Key.Up)
                {
                    target = index == 0 ? owner as TreeViewItem : Realize(owner, index - 1);
                    if (index > 0)
                        while (target is { IsExpanded: true, ItemCount: > 0 }) target = Realize(target, target.ItemCount - 1);
                    break;
                }
                if (index + 1 < owner.ItemCount)
                {
                    target = Realize(owner, index + 1);
                    break;
                }
                if (owner is not TreeViewItem parent) break;
                cursor = parent;
            }
        }
        if (target is null) return;
        tree.SelectedItem = target.DataContext;
        target.BringIntoView();
        target.Focus();
        e.Handled = true;

        TreeViewItem? Realize(ItemsControl owner, int index)
        {
            owner.ScrollIntoView(index);
            tree.UpdateLayout();
            return owner.ContainerFromIndex(index) as TreeViewItem;
        }
    }

    private async void OnCopyInstructionsClick(object? sender, RoutedEventArgs e) => await Tools.CopyInstructionsAsync();

    private async void OnSaveClick(object? sender, RoutedEventArgs e) => await Tools.SaveAsync();

    private async void OnOpenClick(object? sender, RoutedEventArgs e) => await Tools.OpenAsync();
}
