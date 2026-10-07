using Avalonia.Controls;
using Avalonia.Input;
using SIL.Motif.App.Controls;

namespace SIL.Motif.App.Views;

public class CopyableTextBlock : WritingSystemSelectableTextBlock
{
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = false;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = false;
    }
}
