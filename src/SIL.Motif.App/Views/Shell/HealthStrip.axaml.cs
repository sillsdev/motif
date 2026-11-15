using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views.Shell;

/// <summary>The health strip and its keyboard-accessible details.</summary>
public sealed partial class HealthStrip : UserControl
{
    public HealthStrip(HealthStripViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
    }

    private void Open(FlyoutShowMode mode)
    {
        var button = this.FindControl<Button>("HealthButton")!;
        if (button.Flyout is not Flyout flyout) return;
        flyout.ShowMode = mode;
        flyout.ShowAt(button);
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e) =>
        Open(FlyoutShowMode.TransientWithDismissOnPointerMoveAway);

    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional)
            Dispatcher.UIThread.Post(() =>
            {
                if (this.FindControl<Button>("HealthButton")?.IsFocused == true) Open(FlyoutShowMode.Standard);
            });
    }
}
