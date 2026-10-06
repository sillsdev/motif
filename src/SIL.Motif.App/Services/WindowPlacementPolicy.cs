using Avalonia;

namespace SIL.Motif.App.Services;

internal static class WindowPlacementPolicy
{
    public static PixelPoint Constrain(PixelPoint position, PixelRect workArea, Size size, double renderScaling)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0 || !double.IsFinite(renderScaling) || renderScaling <= 0)
            return position;
        var width = Math.Min(workArea.Width, Math.Max(1, (int)Math.Ceiling(size.Width * renderScaling)));
        var height = Math.Min(workArea.Height, Math.Max(1, (int)Math.Ceiling(size.Height * renderScaling)));
        var maxX = Math.Max(workArea.X, workArea.Right - width);
        var maxY = Math.Max(workArea.Y, workArea.Bottom - height);
        return new PixelPoint(Math.Clamp(position.X, workArea.X, maxX), Math.Clamp(position.Y, workArea.Y, maxY));
    }
}
