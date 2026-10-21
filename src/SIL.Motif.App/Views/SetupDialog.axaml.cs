using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>The modal project setup steps, placed over the shell from the main window.</summary>
public sealed partial class SetupDialog : UserControl
{
    public SetupDialog() => AvaloniaXamlLoader.Load(this);

    // The number field commits its text on Enter; the key stays there so the default button does not also advance.
    private void OnLimitKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) e.Handled = true;
    }

    /// <summary>Fits the setup surface inside the window's unscaled client area.</summary>
    /// <param name="maxLogicalWidth">The available width before display zoom is applied.</param>
    /// <param name="maxLogicalHeight">The available height before display zoom is applied.</param>
    public void FitToAvailableArea(double maxLogicalWidth, double maxLogicalHeight)
    {
        // A hidden dialog is never measured, so it has no visual children; its name scope still resolves the surface.
        var surface = this.FindControl<Border>("DialogSurface");
        if (surface is null) return;
        var application = Application.Current;
        var preferredWidth = application?.TryGetResource("Component.Setup.DialogWidth", ActualThemeVariant,
            out var widthValue) == true && widthValue is double width ? width : maxLogicalWidth;
        var preferredHeight = application?.TryGetResource("Component.Setup.DialogHeightLimit", ActualThemeVariant,
            out var heightValue) == true && heightValue is double height ? height : maxLogicalHeight;
        var maxSurfaceWidth = Math.Min(preferredWidth, maxLogicalWidth);
        var maxSurfaceHeight = Math.Min(preferredHeight, maxLogicalHeight);
        if (double.IsFinite(maxSurfaceWidth)) surface.MaxWidth = Math.Max(0, maxSurfaceWidth);
        if (double.IsFinite(maxSurfaceHeight)) surface.MaxHeight = Math.Max(0, maxSurfaceHeight);
    }
}
