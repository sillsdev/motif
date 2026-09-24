using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Timing page: where an Assessment's parse time went, once there is an Assessment.</summary>
public sealed partial class TimingPage : UserControl
{
    public TimingPage(TimingPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ContentControl>("StatisticsHost")!.Content = new StatisticsPanel(page.Statistics);
    }
}
