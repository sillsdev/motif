using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The recorded approved Lost words and shared morphemes beside Overview.</summary>
public sealed partial class OverviewInspector : UserControl
{
    /// <summary>Creates the Inspector over the existing Overview model.</summary>
    public OverviewInspector(OverviewPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>The same summary model as the working page.</summary>
    public OverviewPageModel Page => (OverviewPageModel)DataContext!;
}
