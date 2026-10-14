using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Warnings page's area, severity, and exact-use filters.</summary>
public sealed partial class WarningsFilterPanel : UserControl
{
    /// <summary>Creates the page-owned filter controls for <paramref name="page"/>.</summary>
    public WarningsFilterPanel(WarningsPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Page = page;
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>The page model that owns the filter state.</summary>
    public WarningsPageModel Page { get; }
}
