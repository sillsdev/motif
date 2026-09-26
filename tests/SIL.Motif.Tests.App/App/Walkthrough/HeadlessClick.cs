using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>Clicks a control the way a person does, through the pointer, and fails when the click misses it.</summary>
/// <remarks>
/// Each headless pointer call runs the dispatcher's queued work before it delivers its input, so anything that
/// arrives between aiming and pressing, such as a page's rows loading above the control, can move the control out
/// from under the aimed point; pinned by `RefreshStatisticsAimedBeforeTheTimingArrivesIsMissedAndAimedAfterItLoads`.
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
        void OnPressed(object? sender, PointerPressedEventArgs e) => pressed = true;
        void OnClicked(object? sender, RoutedEventArgs e) => clicked = true;
        control.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
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
            if (control is Button clickable) clickable.Click -= OnClicked;
        }
        Dispatcher.UIThread.RunJobs();

        if (pressed && (clicked || control is not Button)) return;
        var now = CentreOf(window, control);
        var why = pressed ? "the press reached it, but it moved or changed before the release clicked it."
            : now == aimed ? "something covered it, such as an open popup, and took the press."
            : "something moved it after the click was aimed.";
        Assert.Fail($"The click aimed at {aimed} missed '{accessibleName}', which is now at " +
            $"{now?.ToString() ?? "nowhere in the window"}: {why} Wait for whatever is still loading before clicking.");
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
