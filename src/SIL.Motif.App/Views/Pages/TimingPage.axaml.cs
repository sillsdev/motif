using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Timing page: where an Assessment's parse time went, once there is an Assessment.</summary>
public sealed partial class TimingPage : UserControl
{
    public TimingPage(HandoffWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        DataContext = workspace;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ContentControl>("StatisticsHost")!.Content = new StatisticsPanel(workspace.Statistics);
    }
}
