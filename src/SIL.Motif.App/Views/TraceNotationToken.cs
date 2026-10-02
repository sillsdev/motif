using Avalonia.Controls;

namespace SIL.Motif.App.Views;

/// <summary>A notation token whose explanation is reachable by pointer and keyboard.</summary>
public sealed class TraceNotationToken : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    public TraceNotationToken()
    {
        ClearTipPlacement.SetIsEnabled(this, true);
        GotFocus += (_, _) => ToolTip.SetIsOpen(this, true);
        LostFocus += (_, _) => ToolTip.SetIsOpen(this, false);
    }
}
