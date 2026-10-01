using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.VisualTree;

namespace SIL.Motif.App.Views;

/// <summary>
/// Opens a control's tooltip on the first side of it where the tip covers no other control a person could press,
/// focus or type in, and stays inside the window. The sides are tried above, then to the left, then to the right,
/// then below, so a tip prefers to sit over what has already been read. When no side is clear, the tip opens on the
/// side inside the window that covers the fewest controls.
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

    /// <summary>
    /// The first of <see cref="Sides"/> whose tip, of <paramref name="tip"/> size beside <paramref name="owner"/>,
    /// lies inside <paramref name="window"/> and meets the fewest of <paramref name="obstacles"/>, ideally none; the
    /// first side when no side fits inside the window.
    /// </summary>
    internal static (PopupAnchor Anchor, PopupGravity Gravity) Choose(Rect owner, Size tip, Rect window, IReadOnlyList<Rect> obstacles) =>
        Sides.Select(side => (Side: side, Area: Area(owner, tip, side.Anchor, side.Gravity)))
            .Where(candidate => window.Contains(candidate.Area))
            .Select(candidate => (candidate.Side, Covered: obstacles.Count(obstacle => obstacle.Intersects(candidate.Area))))
            .DefaultIfEmpty((Side: Sides[0], Covered: 0))
            .MinBy(candidate => candidate.Covered).Side;

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
        var (anchor, gravity) = Choose(AreaOf(owner, window), placement.PopupSize, new Rect(window.Bounds.Size), obstacles);
        placement.Anchor = anchor;
        placement.Gravity = gravity;
        // The tooltip's own offset would push the tip back onto what the chosen side was clear of.
        placement.Offset = default;
    }

    // What a person could press, focus or type in, other than the owner, what holds it, what it holds, or a tip.
    private static bool IsObstacleTo(Control owner, Control other) =>
        other != owner && IsInteractive(other) && other.IsEffectivelyVisible &&
        !other.GetVisualAncestors().Contains(owner) && !owner.GetVisualAncestors().Contains(other) &&
        !other.GetSelfAndVisualAncestors().OfType<Visual>().Any(visual => visual is ToolTip || visual.Opacity == 0);

    private static bool IsInteractive(Control control) => control is Button or TextBox or ComboBox or NumericUpDown or
        ListBoxItem or TreeViewItem or MenuItem or Slider || control is Border or UserControl && control.Focusable;

    // The part of a control left after every clipping ancestor, such as a scrolled list, has cut it.
    private static Rect Shown(Control control, TopLevel window) =>
        control.GetVisualAncestors().OfType<Visual>().Where(visual => visual.ClipToBounds && visual != window)
            .Aggregate(AreaOf(control, window), (shown, ancestor) => shown.Intersect(AreaOf(ancestor, window)));

    private static Rect AreaOf(Visual visual, TopLevel window) =>
        new(visual.TranslatePoint(default, window) ?? default, visual.Bounds.Size);
}
