using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.Views;
using Xunit;
using ModuleWordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;
using ModuleWordRowHeader = SIL.Motif.App.Controls.WordPresentation.WordRowHeader;

namespace SIL.Motif.Tests.App;

internal static class WordListHeaderAssertions
{
    public static void FirstRowIsBelowHeader(WordListSlot slot, ModuleWordRow row)
    {
        var header = Assert.Single(slot.GetVisualDescendants().OfType<ModuleWordRowHeader>());
        AssertDoesNotIntersect(slot, row, header);
    }

    private static void AssertDoesNotIntersect(WordListSlot slot, Visual row, Control header)
    {
        Assert.True(header.IsEffectivelyVisible, "The list header is not visible.");
        Assert.True(row.IsEffectivelyVisible, "The word row is not visible.");
        var headerOrigin = header.TranslatePoint(default, slot) ??
            throw new InvalidOperationException("The header is outside its list slot.");
        var headerBounds = new Rect(headerOrigin, header.Bounds.Size);
        var visibleRowBounds = VisibleBounds(row, slot);
        Assert.True(visibleRowBounds.Width > 0 && visibleRowBounds.Height > 0,
            "The row has no visible area in its word-list slot.");
        Assert.False(Intersects(headerBounds, visibleRowBounds),
            $"The visible row at {visibleRowBounds} intersects the header at {headerBounds} in the word-list slot.");
    }

    private static Rect VisibleBounds(Visual control, WordListSlot slot)
    {
        var origin = control.TranslatePoint(default, slot) ??
            throw new InvalidOperationException("The row is outside its list slot.");
        var bounds = new Rect(origin, control.Bounds.Size);
        foreach (var visual in control.GetSelfAndVisualAncestors())
        {
            if (visual.ClipToBounds)
            {
                var clipOrigin = visual.TranslatePoint(default, slot);
                if (clipOrigin is null) return default;
                bounds = Intersect(bounds, new Rect(clipOrigin.Value, visual.Bounds.Size));
            }
            if (ReferenceEquals(visual, slot)) break;
        }
        return bounds;
    }

    private static Rect Intersect(Rect first, Rect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        return right <= left || bottom <= top ? default : new Rect(left, top, right - left, bottom - top);
    }

    private static bool Intersects(Rect first, Rect second) =>
        first.Left < second.Right && second.Left < first.Right &&
        first.Top < second.Bottom && second.Top < first.Bottom;
}
