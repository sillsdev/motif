using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The statistics grid, bound to its own <see cref="Statistics"/> view model.</summary>
public sealed partial class StatisticsPanel : UserControl
{
    public StatisticsPanel(StatisticsViewModel statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        Statistics = statistics;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
        Statistics.PropertyChanged += OnStatisticsChanged;
        ShowColumnsForGroup();
    }

    public StatisticsViewModel Statistics { get; }

    private void OnStatisticsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StatisticsViewModel.SelectedGroup) or nameof(StatisticsViewModel.AnyPasses)) ShowColumnsForGroup();
    }

    // Each grouping shows only its own columns, and Passes only once some row needed one.
    private void ShowColumnsForGroup()
    {
        var words = Statistics.SelectedGroup == "word";
        foreach (var column in this.FindControl<DataGrid>("Grid")!.Columns)
        {
            column.IsVisible = column.Tag switch
            {
                "kind" or "object" => !words,
                "word" or "completion" => words,
                "passes" => Statistics.AnyPasses,
                _ => true,
            };
        }
    }

    private void OnOpenTimeLimitClick(object? sender, RoutedEventArgs e) => Statistics.OpenTimeLimit?.Invoke();

    private void OnTrySlowestClick(object? sender, RoutedEventArgs e)
    {
        if (Statistics.SlowestWord?.Word is { } word) Statistics.TryWord?.Invoke(word);
    }

    private void OnAllRowsClick(object? sender, RoutedEventArgs e) => Statistics.OnlyIncomplete = false;

    private void OnOnlyIncompleteClick(object? sender, RoutedEventArgs e) => Statistics.OnlyIncomplete = true;

    // Handled so the grid does not also reorder items itself; the view model owns sort state.
    private void OnGridSorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;
        if (e.Column.Tag is string column) Statistics.SortBy(column);
    }
}
