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
        Page = page;
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ContentControl>("StatisticsHost")!.Content = new StatisticsPanel(page.Statistics);
    }

    /// <summary>The page model used by row templates to choose a rule.</summary>
    public TimingPageModel Page { get; }
}
