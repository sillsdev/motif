using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.VisualTree;

namespace SIL.Motif.App.Views;

/// <summary>
/// Opens a control's tooltip on the first side of it where the tip covers no other control a person could press,
/// focus or type in, and stays inside the window. The sides are tried above, then to the left, then to the right,
/// then below, so a tip prefers to sit over what has already been read. When no side is clear and the owner sits
/// inside a control a person could press or focus that is no taller than a few tips, such as a multi-row strip, the
/// tip may instead hang above or below that whole control, level with the owner, where its neighbours do not reach.
/// When no side is clear, the tip opens on the side inside the window that covers the fewest controls.
/// </summary>
/// <remarks>
/// Set <c>IsEnabled</c> on a tooltip owner, in its view or from a component style. It replaces the owner's
/// <c>ToolTip.Placement</c> with a custom placement that measures the window each time the tip opens, so the choice
/// follows the window's width and what the page shows at that moment.
/// </remarks>
public static class ClearTipPlacement
{
    /// <summary>Whether the owner's tooltip opens where it covers no other control.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(ClearTipPlacement));

    /// <summary>The control whose edges the tooltip clears instead of its clickable owner.</summary>
    public static readonly AttachedProperty<Control?> AnchorProperty =
        AvaloniaProperty.RegisterAttached<Control, Control?>("Anchor", typeof(ClearTipPlacement));

    /// <summary>Additional nonnegative space around the anchor, in device-independent pixels.</summary>
    public static readonly AttachedProperty<double> ClearanceProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("Clearance", typeof(ClearTipPlacement),
            validate: value => double.IsFinite(value) && value >= 0);

    /// <summary>The sides tried, in order: the anchor edge or corner of the owner, and the way the tip grows from it.</summary>
    internal static IReadOnlyList<(PopupAnchor Anchor, PopupGravity Gravity)> Sides { get; } =
    [
        (PopupAnchor.Top, PopupGravity.Top),
        (PopupAnchor.TopRight, PopupGravity.TopLeft),
        (PopupAnchor.TopLeft, PopupGravity.TopRight),
        (PopupAnchor.Left, PopupGravity.Left),
        (PopupAnchor.TopLeft, PopupGravity.BottomLeft),
        (PopupAnchor.BottomLeft, PopupGravity.TopLeft),
        (PopupAnchor.Right, PopupGravity.Right),
        (PopupAnchor.TopRight, PopupGravity.BottomRight),
        (PopupAnchor.BottomRight, PopupGravity.TopRight),
        (PopupAnchor.Bottom, PopupGravity.Bottom),
        (PopupAnchor.BottomRight, PopupGravity.BottomLeft),
        (PopupAnchor.BottomLeft, PopupGravity.BottomRight),
    ];

    /// <summary>The sides that hang a tip above or below the whole line its owner sits in, level with the owner.</summary>
    internal static IReadOnlyList<(PopupAnchor Anchor, PopupGravity Gravity)> LineSides { get; } =
    [
        (PopupAnchor.Top, PopupGravity.Top),
        (PopupAnchor.TopRight, PopupGravity.TopLeft),
        (PopupAnchor.TopLeft, PopupGravity.TopRight),
        (PopupAnchor.Bottom, PopupGravity.Bottom),
        (PopupAnchor.BottomRight, PopupGravity.BottomLeft),
        (PopupAnchor.BottomLeft, PopupGravity.BottomRight),
    ];

    static ClearTipPlacement()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>((owner, change) =>
        {
            if (change.GetNewValue<bool>())
            {
                ToolTip.SetPlacement(owner, PlacementMode.Custom);
                ToolTip.SetCustomPopupPlacementCallback(owner, Place);
            }
            else
            {
                owner.ClearValue(ToolTip.PlacementProperty);
                owner.ClearValue(ToolTip.CustomPopupPlacementCallbackProperty);
            }
        });
    }

    /// <summary>Gets whether <paramref name="owner"/>'s tooltip opens where it covers no other control.</summary>
    public static bool GetIsEnabled(Control owner) => owner.GetValue(IsEnabledProperty);

    /// <summary>Sets whether <paramref name="owner"/>'s tooltip opens where it covers no other control.</summary>
    public static void SetIsEnabled(Control owner, bool value) => owner.SetValue(IsEnabledProperty, value);

    /// <summary>Gets the control used for placement, or null to use the tooltip owner.</summary>
    public static Control? GetAnchor(Control owner) => owner.GetValue(AnchorProperty);

    /// <summary>Sets the control whose edges the tooltip clears.</summary>
    public static void SetAnchor(Control owner, Control? anchor) => owner.SetValue(AnchorProperty, anchor);

    /// <summary>Gets the additional space the tooltip leaves around its anchor.</summary>
    public static double GetClearance(Control owner) => owner.GetValue(ClearanceProperty);

    /// <summary>Sets finite, nonnegative space the tooltip leaves around its anchor.</summary>
    public static void SetClearance(Control owner, double value) => owner.SetValue(ClearanceProperty, value);

    /// <summary>
    /// The first of <see cref="Sides"/> whose tip, of <paramref name="tip"/> size beside <paramref name="owner"/>,
    /// lies inside <paramref name="window"/> and meets the fewest of <paramref name="obstacles"/>, ideally none; the
    /// first side when no side fits inside the window.
    /// </summary>
    internal static (PopupAnchor Anchor, PopupGravity Gravity) Choose(Rect owner, Size tip, Rect window, IReadOnlyList<Rect> obstacles) =>
        Rank(owner, tip, window, obstacles, Sides) is { } best ? best.Side : Sides[0];

    /// <summary>The tallest line, in tip heights, a tip may hang above or below; a taller one is a container, not a strip.</summary>
    internal const double MaxLineTips = 4;

    /// <summary>
    /// Where a tip goes: the <see cref="Choose"/> side of <paramref name="owner"/> when it covers nothing, otherwise the
    /// <see cref="LineSides"/> side of the column <paramref name="owner"/> makes through <paramref name="line"/>, widened
    /// vertically by <paramref name="clearance"/>, when that covers fewer controls. A <paramref name="line"/> taller than
    /// <see cref="MaxLineTips"/> tips is ignored. The anchor is the rectangle the side is measured from.
    /// </summary>
    internal static (Rect Anchor, PopupAnchor Side, PopupGravity Gravity) Plan(
        Rect owner, Rect? line, Size tip, Rect window, IReadOnlyList<Rect> obstacles, double clearance = 0)
    {
        var near = Rank(owner, tip, window, obstacles, Sides) ?? (Sides[0], 0);
        if (near.Covered > 0 && line is { Height: > 0 } band && band.Height <= MaxLineTips * tip.Height)
        {
            var column = new Rect(owner.X, band.Top - clearance, owner.Width, band.Height + 2 * clearance);
            if (Rank(column, tip, window, obstacles, LineSides) is { } beyond && beyond.Covered < near.Covered)
                return (column, beyond.Side.Anchor, beyond.Side.Gravity);
        }
        return (owner, near.Side.Anchor, near.Side.Gravity);
    }

    // The first of the sides inside the window that covers the fewest obstacles; null when none fits.
    private static ((PopupAnchor Anchor, PopupGravity Gravity) Side, int Covered)? Rank(Rect owner, Size tip, Rect window,
        IReadOnlyList<Rect> obstacles, IReadOnlyList<(PopupAnchor Anchor, PopupGravity Gravity)> sides)
    {
        var fitting = sides.Select(side => (Side: side, Area: Area(owner, tip, side.Anchor, side.Gravity)))
            .Where(candidate => window.Contains(candidate.Area))
            .Select(candidate => (candidate.Side, Covered: obstacles.Count(obstacle => Overlaps(obstacle, candidate.Area))))
            .ToList();
        return fitting.Count == 0 ? null : fitting.MinBy(candidate => candidate.Covered);
    }

    private static bool Overlaps(Rect first, Rect second) =>
        first.Left < second.Right && first.Right > second.Left &&
        first.Top < second.Bottom && first.Bottom > second.Top;

    /// <summary>Where a tip of <paramref name="tip"/> size lands when it grows from <paramref name="anchor"/> of <paramref name="owner"/>.</summary>
    internal static Rect Area(Rect owner, Size tip, PopupAnchor anchor, PopupGravity gravity)
    {
        var x = anchor.HasFlag(PopupAnchor.Left) ? owner.Left : anchor.HasFlag(PopupAnchor.Right) ? owner.Right : owner.Center.X;
        var y = anchor.HasFlag(PopupAnchor.Top) ? owner.Top : anchor.HasFlag(PopupAnchor.Bottom) ? owner.Bottom : owner.Center.Y;
        x -= gravity.HasFlag(PopupGravity.Left) ? tip.Width : gravity.HasFlag(PopupGravity.Right) ? 0 : tip.Width / 2;
        y -= gravity.HasFlag(PopupGravity.Top) ? tip.Height : gravity.HasFlag(PopupGravity.Bottom) ? 0 : tip.Height / 2;
        return new Rect(new Point(x, y), tip);
    }

    private static void Place(CustomPopupPlacement placement)
    {
        if (placement.Target is not Control owner || TopLevel.GetTopLevel(owner) is not { } window) return;
        var obstacles = window.GetVisualDescendants().OfType<Control>()
            .Where(other => IsObstacleTo(owner, other))
            .Select(other => Shown(other, window))
            .Where(shown => shown.Width > 0 && shown.Height > 0)
            .ToList();
        var target = GetAnchor(owner) ?? owner;
        var area = AreaOf(target, window);
        var clearance = GetClearance(owner);
        if (clearance > 0) area = area.Inflate(clearance).Intersect(new Rect(window.Bounds.Size));
        var (from, anchor, gravity) = Plan(area, LineOf(owner, window), placement.PopupSize, new Rect(window.Bounds.Size), obstacles, clearance);
        if (target != owner || clearance > 0 || from != area) placement.AnchorRectangle = from;
        placement.Anchor = anchor;
        placement.Gravity = gravity;
        // The tooltip's own offset would push the tip back onto what the chosen side was clear of.
        placement.Offset = default;
    }

    /// <summary>
    /// The visible area of the nearest control around <paramref name="owner"/> that a person could press or focus,
    /// cut by every scrolled or clipped ancestor the way an obstacle is; null when none is visible.
    /// </summary>
    internal static Rect? LineOf(Control owner, TopLevel window) =>
        owner.GetVisualAncestors().OfType<Control>().FirstOrDefault(IsInteractive) is { } line &&
        Shown(line, window) is { Width: > 0, Height: > 0 } shown ? shown : null;

    // What a person could press, focus or type in, other than the owner, what holds it, what it holds, or a tip.
    private static bool IsObstacleTo(Control owner, Control other) =>
        other != owner && IsInteractive(other) && other.IsEffectivelyVisible &&
        !other.GetVisualAncestors().Contains(owner) && !owner.GetVisualAncestors().Contains(other) &&
        !other.GetSelfAndVisualAncestors().OfType<Visual>().Any(visual => visual is ToolTip || visual.Opacity == 0);

    private static bool IsInteractive(Control control) => control is Button or SplitButton or TextBox or ComboBox or
        NumericUpDown or ListBoxItem or TreeViewItem or MenuItem or Slider or ScrollBar ||
        control is Border or UserControl && control.Focusable;

    // The part of a control left after every clipping ancestor, such as a scrolled list, has cut it.
    private static Rect Shown(Control control, TopLevel window) =>
        control.GetVisualAncestors().OfType<Visual>().Where(visual => visual.ClipToBounds && visual != window)
            .Aggregate(AreaOf(control, window), (shown, ancestor) => shown.Intersect(AreaOf(ancestor, window)));

    internal static Rect AreaOf(Visual visual, TopLevel window)
    {
        var transform = visual.TransformToVisual(window);
        return transform is { } matrix ? TransformBounds(new Rect(visual.Bounds.Size), matrix) : default;
    }

    internal static Rect TransformBounds(Rect bounds, Matrix transform)
    {
        var corners = new[]
        {
            transform.Transform(bounds.TopLeft),
            transform.Transform(bounds.TopRight),
            transform.Transform(bounds.BottomLeft),
            transform.Transform(bounds.BottomRight),
        };
        var left = corners.Min(point => point.X);
        var top = corners.Min(point => point.Y);
        var right = corners.Max(point => point.X);
        var bottom = corners.Max(point => point.Y);
        return new Rect(left, top, right - left, bottom - top);
    }
}
