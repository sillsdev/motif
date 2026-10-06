using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>Clicks a control the way a person does, through the pointer, and fails when the click misses it.</summary>
/// <remarks>
/// Each headless pointer call runs the dispatcher's queued work before it delivers its input, so anything that
/// arrives between aiming and pressing, such as a page's rows loading above the control, can move the control out
/// from under the aimed point; pinned by `DetailedStatisticsLoadWhenOpenedWithoutASeparateRefreshButton`.
/// The press then lands on whatever took its place. Checking that the press reached the control, and for a
/// <see cref="Button"/> that it clicked, turns that into a failure naming the miss.
/// </remarks>
internal static class HeadlessClick
{
    public static void Click(TopLevel window, Control control, string accessibleName)
    {
        var aimed = Aim(window, control, accessibleName);
        var pressed = false;
        var clicked = false;
        var toggle = control as ToggleButton;
        var initialToggleState = toggle?.IsChecked;
        Visual? pressedSource = null;
        void OnPressed(object? sender, PointerPressedEventArgs e) => pressed = true;
        void OnWindowPressed(object? sender, PointerPressedEventArgs e) => pressedSource = e.Source as Visual;
        void OnClicked(object? sender, RoutedEventArgs e) => clicked = true;
        control.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerPressedEvent, OnWindowPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (control is Button button) button.Click += OnClicked;
        try
        {
            window.MouseMove(aimed);
            window.MouseDown(aimed, MouseButton.Left);
            window.MouseUp(aimed, MouseButton.Left);
        }
        finally
        {
            control.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
            window.RemoveHandler(InputElement.PointerPressedEvent, OnWindowPressed);
            if (control is Button clickable) clickable.Click -= OnClicked;
        }
        Dispatcher.UIThread.RunJobs();

        var toggled = toggle is not null && toggle.IsChecked != initialToggleState;
        if (pressed && (clicked || toggled || control is not Button && toggle is null)) return;
        var now = CentreOf(window, control);
        var why = pressed ? "the press reached it, but it moved or changed before the release clicked it."
            : now == aimed ? "something covered it, such as an open popup, and took the press."
            : "something moved it after the click was aimed.";
        var overlays = window.GetVisualDescendants().OfType<Control>()
            .Where(candidate => candidate != control && candidate.IsEffectivelyVisible)
            .Select(candidate => (Control: candidate, Origin: candidate.TranslatePoint(new Point(), window)))
            .Where(item => item.Origin is { } origin &&
                new Rect(origin, item.Control.Bounds.Size).Contains(aimed))
            .Select(item => $"{item.Control.GetType().Name} name='{item.Control.Name}' " +
                $"automation='{Avalonia.Automation.AutomationProperties.GetName(item.Control)}' " +
                $"content='{(item.Control as Button)?.Content}' " +
                $"bounds={new Rect(item.Origin!.Value, item.Control.Bounds.Size)} " +
                $"topLevel={TopLevel.GetTopLevel(item.Control)?.GetType().Name}")
            .TakeLast(12);
        var presenter = control.FindAncestorOfType<FlyoutPresenter>();
        var presenterOrigin = presenter?.TranslatePoint(new Point(), window);
        var settingsButton = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
            Avalonia.Automation.AutomationProperties.GetName(button) == "Settings");
        var settingsOrigin = settingsButton?.TranslatePoint(new Point(), window);
        Assert.Fail($"The click aimed at {aimed} missed '{accessibleName}', which is now at " +
            $"{now?.ToString() ?? "nowhere in the window"}: {why} " +
            $"Target bounds: {control.Bounds}; input root: {window.GetType().Name}; " +
            $"target top level: {TopLevel.GetTopLevel(control)?.GetType().Name ?? "none"}; " +
            $"window position: {(window is Window owner ? owner.Position.ToString() : "unavailable")}; " +
            $"window size: {window.ClientSize}. " +
            $"flyout presenter: {(presenterOrigin is { } presenterPoint ? new Rect(presenterPoint, presenter!.Bounds.Size).ToString() : "none")}; " +
            $"Settings button origin: {settingsOrigin?.ToString() ?? "none"}. " +
            $"Pointer press source: {pressedSource?.GetType().Name ?? "none"}. " +
            $"Controls over the point: {string.Join("; ", overlays)}. Wait for whatever is still loading before clicking.");
    }

    /// <summary>Presses and releases twice over <paramref name="control"/>, as a double-click does, and returns its clicks.</summary>
    public static int DoubleClick(TopLevel window, Control control, string accessibleName)
    {
        var aimed = Aim(window, control, accessibleName);
        var clicks = 0;
        void OnClicked(object? sender, RoutedEventArgs e) => clicks++;
        if (control is Button button) button.Click += OnClicked;
        try
        {
            window.MouseMove(aimed);
            window.MouseDown(aimed, MouseButton.Left);
            window.MouseUp(aimed, MouseButton.Left);
            window.MouseDown(aimed, MouseButton.Left);
            window.MouseUp(aimed, MouseButton.Left);
        }
        finally
        {
            if (control is Button clickable) clickable.Click -= OnClicked;
        }
        Dispatcher.UIThread.RunJobs();
        return clicks;
    }

    /// <summary>
    /// Presses where <paramref name="control"/> is without requiring the press to reach it, as when an open popup's
    /// light dismiss takes the press; the caller checks the effect it expects instead.
    /// </summary>
    public static void PressOver(TopLevel window, Control control, string accessibleName)
    {
        var aimed = Aim(window, control, accessibleName);
        window.MouseMove(aimed);
        window.MouseDown(aimed, MouseButton.Left);
        window.MouseUp(aimed, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Point Aim(TopLevel window, Control control, string accessibleName)
    {
        Assert.True(control.IsEffectivelyEnabled, $"'{accessibleName}' is not effectively enabled.");
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return CentreOf(window, control)
            ?? throw new InvalidOperationException($"'{accessibleName}' is not positioned in the walkthrough window.");
    }

    private static Point? CentreOf(TopLevel window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
}
