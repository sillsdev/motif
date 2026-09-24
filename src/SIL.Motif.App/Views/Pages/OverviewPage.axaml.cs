using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Overview page: where the project stands, shown today by the project summary panel.</summary>
public sealed partial class OverviewPage : UserControl
{
    public OverviewPage(OverviewPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ContentControl>("ProjectHost")!.Content = new ProjectPanel(page);
    }
}
