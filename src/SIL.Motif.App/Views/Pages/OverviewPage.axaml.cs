using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The project summary page, built from its response and its own page model.</summary>
public sealed partial class OverviewPage : UserControl
{
    public OverviewPage(OverviewPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>The Overview model used by its project chooser.</summary>
    public OverviewPageModel Page => (OverviewPageModel)DataContext!;
}
