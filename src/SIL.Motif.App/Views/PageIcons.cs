using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The sidebar's line icons, one per <see cref="WorkspacePage"/>, drawn on a 24-unit grid as outlines to be
/// stroked in the current text colour so they read the same in light and dark themes.
/// </summary>
public static class PageIcons
{
    /// <summary>Converts a <see cref="WorkspacePage"/> to its icon's geometry, for a <c>Path</c>'s data.</summary>
    public static readonly IValueConverter Geometry =
        new FuncValueConverter<WorkspacePage, Geometry>(page => For(page));

    /// <summary>The AI Handoff's sparkles, also used beside prompts that start one.</summary>
    public static Geometry Sparkles => For(WorkspacePage.AiHandoff);

    /// <summary>The outline for <paramref name="page"/>.</summary>
    public static Geometry For(WorkspacePage page) => page switch
    {
        WorkspacePage.Overview => Group(
            Rect(3.5, 3.5, 7, 9), Rect(13.5, 3.5, 7, 5), Rect(13.5, 11.5, 7, 9), Rect(3.5, 15.5, 7, 5)),
        WorkspacePage.Texts => Group(
            Parse("M3 5h6a3 3 0 0 1 3 3v12a2 2 0 0 0-2-2H3z"), Parse("M21 5h-6a3 3 0 0 0-3 3v12a2 2 0 0 1 2-2h7z")),
        WorkspacePage.TryAWord => Group(
            Circle(10.5, 10.5, 6.5), Parse("M15.5 15.5L21 21"), Parse("M7.5 10.5h6M10.5 8v5")),
        WorkspacePage.Timing => Group(Circle(12, 13, 7), Parse("M12 13V9M10 3h4M12 3v3M18 6l1.5-1.5")),
        WorkspacePage.Warnings => Group(Parse("M12 4l9 16H3z"), Parse("M12 10v4M12 17v.5")),
        WorkspacePage.Review => Group(Parse("M7 4v16M17 4v16"), Parse("M4 8l3-3 3 3M14 16l3 3 3-3")),
        WorkspacePage.AiHandoff => Group(
            Parse("M12 3l1.8 4.8L18 9.5l-4.2 1.7L12 16l-1.8-4.8L6 9.5l4.2-1.7z"),
            Parse("M18 15l.8 2 2 .8-2 .8-.8 2-.8-2-2-.8 2-.8z")),
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
    };

    private static GeometryGroup Group(params Geometry[] parts)
    {
        var group = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var part in parts) group.Children.Add(part);
        return group;
    }

    private static RectangleGeometry Rect(double x, double y, double width, double height) =>
        new(new Rect(x, y, width, height), 1.5, 1.5);

    private static EllipseGeometry Circle(double x, double y, double radius) =>
        new(new Rect(x - radius, y - radius, radius * 2, radius * 2));

    private static Geometry Parse(string data) => StreamGeometry.Parse(data);
}
