using Avalonia.Controls;

namespace SIL.Motif.App.Views;

/// <summary>
/// Keeps a scroll viewer at its end while its content grows underneath, so a reader who pressed End, or a focus
/// move that scrolled to the last line, still sees the last line after later pages change the content's height.
/// </summary>
/// <remarks>
/// The viewer counts as left at the end once its offset reaches the bottom, including a bottom that moved in the same
/// layout pass that took it there. A later extent change that does not move the offset up scrolls it back to the end.
/// Scrolling up, or any offset short of the end that the extent did not cause, releases it.
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
        viewer.ScrollChanged += (_, e) =>
        {
            var end = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
            var previousEnd = end - e.ExtentDelta.Y + e.ViewportDelta.Y;
            var reachedPreviousEnd = e.OffsetDelta.Y > 0 && viewer.Offset.Y >= previousEnd - Tolerance;
            if ((atEnd || reachedPreviousEnd) && e.ExtentDelta.Y != 0 && e.OffsetDelta.Y >= 0 &&
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
