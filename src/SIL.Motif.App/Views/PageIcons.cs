using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SIL.Motif.App.Views;

/// <summary>
/// Builds the sidebar's line icons as path data on a 24-unit grid, to be stroked in the current text colour so
/// they read the same in light and dark themes. Each page's icon is written in its <see cref="PageRegistry"/>
/// entry with these helpers.
/// </summary>
public static class PageIcons
{
    /// <summary>Converts an icon's path data to the geometry a <c>Path</c> strokes.</summary>
    public static readonly IValueConverter Geometry = new FuncValueConverter<string?, Geometry?>(
        data => string.IsNullOrWhiteSpace(data) ? null : ToGeometry(data));

    /// <summary>The geometry <paramref name="data"/> draws.</summary>
    public static Geometry ToGeometry(string data) => StreamGeometry.Parse(data);

    /// <summary>Joins several outlines into one icon.</summary>
    public static string Of(params string[] parts) => string.Join(" ", parts);

    /// <summary>A rectangle with corners rounded by 1.5 units, as the design's icons draw them.</summary>
    public static string Rect(double x, double y, double width, double height)
    {
        const double r = 1.5;
        var across = width - 2 * r;
        var down = height - 2 * r;
        return Format($"M{x + r} {y}h{across}a{r} {r} 0 0 1 {r} {r}v{down}a{r} {r} 0 0 1 {-r} {r}h{-across}") +
            Format($"a{r} {r} 0 0 1 {-r} {-r}v{-down}a{r} {r} 0 0 1 {r} {-r}z");
    }

    /// <summary>A circle centred on (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static string Circle(double x, double y, double radius) =>
        Format($"M{x - radius} {y}a{radius} {radius} 0 1 0 {2 * radius} 0a{radius} {radius} 0 1 0 {-2 * radius} 0z");

    private static string Format(FormattableString path) => path.ToString(CultureInfo.InvariantCulture);
}
