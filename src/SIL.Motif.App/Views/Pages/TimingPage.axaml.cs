using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Timing page: where an Assessment's parse time went, once there is an Assessment.</summary>
public sealed partial class TimingPage : UserControl
{
    private const double SidePanelLayoutWidth = 900;
    private Grid? _shareLayout;
    private Control? _sidePanel;

    public TimingPage(TimingPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Page = page;
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        _shareLayout = this.FindControl<Grid>("TimingShareLayout");
        _sidePanel = this.FindControl<Control>("TimingSidePanel");
        _shareLayout!.SizeChanged += (_, args) => ArrangeSidePanel(args.NewSize.Width);
        ArrangeSidePanel(_shareLayout.Bounds.Width);
        this.FindControl<ContentControl>("StatisticsHost")!.Content = new StatisticsPanel(page.Statistics);
    }

    /// <summary>The page model used by row templates to choose a rule.</summary>
    public TimingPageModel Page { get; }

    private async void OnStatisticsExpanded(object? sender, RoutedEventArgs e)
    {
        var statistics = Page.Statistics;
        if (statistics.HasLoaded || statistics.LoadCommand.IsRunning) return;
        await statistics.LoadCommand.ExecuteAsync(null);
    }

    private void ArrangeSidePanel(double width)
    {
        if (_shareLayout is null || _sidePanel is null) return;
        var compact = width < SidePanelLayoutWidth;
        Grid.SetColumn(_sidePanel, compact ? 0 : 1);
        Grid.SetRow(_sidePanel, compact ? 1 : 0);
        Grid.SetColumnSpan(_sidePanel, compact ? 2 : 1);
        var hasWideClass = _sidePanel.Classes.Contains("wide");
        if (!compact && !hasWideClass) _sidePanel.Classes.Add("wide");
        else if (compact && hasWideClass) _sidePanel.Classes.Remove("wide");
    }
}
