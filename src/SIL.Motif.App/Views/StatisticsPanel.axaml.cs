using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The split statistics grid and rendered summary, bound to its own <see cref="Statistics"/> view model.</summary>
public sealed partial class StatisticsPanel : UserControl
{
    public StatisticsPanel(StatisticsViewModel statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        Statistics = statistics;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public StatisticsViewModel Statistics { get; }

    // Handled so the grid does not also reorder items itself; the view model owns sort state.
    private void OnAllRowsClick(object? sender, RoutedEventArgs e) => Statistics.OnlyIncomplete = false;

    private void OnOnlyIncompleteClick(object? sender, RoutedEventArgs e) => Statistics.OnlyIncomplete = true;

    private void OnGridSorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;
        if (e.Column.Tag is string column) Statistics.SortBy(column);
    }
}
