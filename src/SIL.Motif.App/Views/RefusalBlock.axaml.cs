using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// Shows a <see cref="WindowRefusal"/> the one way every page shows a refusal: its sentence, with the
/// command's own account folded under Details. Bind the refusal as its data context; with none it hides.
/// </summary>
public sealed partial class RefusalBlock : UserControl
{
    public RefusalBlock()
    {
        AvaloniaXamlLoader.Load(this);
        IsVisible = false;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        IsVisible = DataContext is WindowRefusal;
    }
}
