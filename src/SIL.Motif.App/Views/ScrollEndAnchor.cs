using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SIL.Motif.App.Views;

/// <summary>
/// Keeps a scroll viewer at its end while its content grows underneath, so a reader who pressed End, or a focus
/// move that scrolled to the last line, still sees the last line after later pages change the content's height.
/// </summary>
/// <remarks>
/// The viewer counts as left at the end once its offset reaches the bottom, including a bottom that moved in the same
/// layout pass that took it there. A later extent change scrolls it back to the end, even when a virtualizing panel
/// shifted the offset up by no more than that change to keep its realized items in place. A wheel, pointer or key on
/// the viewer that moves it up releases it, as does any other move up that the extent change does not account for.
/// </remarks>
internal static class ScrollEndAnchor
{
    private const double Tolerance = 0.5;

    /// <summary>Starts keeping <paramref name="viewer"/> at its end whenever it is left there.</summary>
    /// <param name="viewer">The vertically scrolling viewer.</param>
    public static void Attach(ScrollViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var atEnd = false;
        var input = false;
        void Input(object? sender, RoutedEventArgs e) => input = true;
        const RoutingStrategies routes = RoutingStrategies.Tunnel | RoutingStrategies.Bubble;
        viewer.AddHandler(InputElement.PointerWheelChangedEvent, Input, routes, handledEventsToo: true);
        viewer.AddHandler(InputElement.PointerPressedEvent, Input, routes, handledEventsToo: true);
        viewer.AddHandler(InputElement.KeyDownEvent, Input, routes, handledEventsToo: true);
        viewer.ScrollChanged += (_, e) =>
        {
            var scrolledUpByInput = input && e.OffsetDelta.Y < 0;
            input = false;
            var end = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
            var previousEnd = end - e.ExtentDelta.Y + e.ViewportDelta.Y;
            var reachedPreviousEnd = e.OffsetDelta.Y > 0 && viewer.Offset.Y >= previousEnd - Tolerance;
            var layoutMove = e.OffsetDelta.Y >= 0 || -e.OffsetDelta.Y <= Math.Abs(e.ExtentDelta.Y) + Tolerance;
            if (!scrolledUpByInput && (atEnd || reachedPreviousEnd) && e.ExtentDelta.Y != 0 && layoutMove &&
                viewer.Offset.Y < end - Tolerance)
            {
                atEnd = true;
                viewer.ScrollToEnd();
                return;
            }
            atEnd = end > 0 && viewer.Offset.Y >= end - Tolerance;
        };
    }
}
