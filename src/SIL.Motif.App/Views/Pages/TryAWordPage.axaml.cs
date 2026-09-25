using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The standalone Try a Word page with its own recent words and workspace links.</summary>
public sealed partial class TryAWordPage : UserControl
{
    /// <summary>Builds the Try a Word page from its model.</summary>
    /// <param name="model">The page model shared with the Texts assessment.</param>
    public TryAWordPage(TryWordPageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ContentControl>("TryWordPanelHost")!.Content = new TryWordPanel(model);
    }
}
