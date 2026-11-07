using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SIL.TextEditing.Avalonia;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>ADR 0021: PageUp and PageDown and auto-scroll while dragging work inside the field; scrolling the view that shows the
    /// field stays with the host, which the field asks (the playground's scroll viewer answers when nothing else does).</summary>
    public class PagingPlaygroundTests
    {
        static ScrollViewer ScrollerOf(Session s) => s.Field.GetVisualAncestors().OfType<ScrollViewer>().First();

        static TopLevel WindowOf(Session s) => TopLevel.GetTopLevel(s.Field)!;

        /// <summary>The caret's top, in window coordinates.</summary>
        static double CaretY(Session s) => s.Field.TranslatePoint(new Point(0, s.Field.CaretRect().Y), WindowOf(s))!.Value.Y;

        [Fact]
        public void PageDown_moves_the_caret_by_the_visible_height_keeps_its_place_on_the_screen_and_PageUp_comes_back() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Long))
            {
                var scroller = ScrollerOf(s);
                s.Caret(10, 4);
                Headless.Pump();
                double page = s.Field.VisibleBounds.Height;
                Assert.True(page > 200);
                var before = s.Field.CaretRect();
                double screenBefore = CaretY(s);
                double offsetBefore = scroller.Offset.Y;
                var oldCaret = s.Editor.Caret;

                s.Press(Key.PageDown);
                var after = s.Field.CaretRect();
                Assert.True(s.Editor.Caret.Paragraph > oldCaret.Paragraph);
                Assert.True(Math.Abs(after.Y - before.Y - page) <= 40, "moved " + (after.Y - before.Y) + " for a page of " + page);
                // The same place along the line, give or take a character, when the line it lands on is a long left-to-right one.
                if (s.Editor.Caret.Paragraph % 5 < 3) Assert.True(Math.Abs(before.X - after.X) <= 40, "x " + before.X + " then " + after.X + " in paragraph " + s.Editor.Caret.Paragraph);
                Assert.Equal(after.Y - before.Y, scroller.Offset.Y - offsetBefore, 1);
                Assert.InRange(CaretY(s), screenBefore - 1, screenBefore + 1);   // the view followed: the caret did not move on the screen

                s.Press(Key.PageUp);
                Assert.True(Math.Abs(s.Field.CaretRect().Y - before.Y) <= 40);
                Assert.InRange(CaretY(s), screenBefore - 1, screenBefore + 1);
            }
        });

        [Fact]
        public void Shift_extends_the_selection_by_a_page_Ctrl_goes_to_the_corners_of_what_shows_and_the_ends_of_the_text_stop_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Long))
            {
                s.Caret(10, 4);
                var anchor = s.Editor.Caret;
                s.Press(Key.PageDown, KeyModifiers.Shift);
                Assert.Equal(anchor, s.Editor.Anchor);
                Assert.True(s.Editor.HasSelection);
                Assert.True(s.Editor.Caret.Paragraph > anchor.Paragraph);
                s.Press(Key.PageUp, KeyModifiers.Shift);
                Assert.True(s.Editor.Caret.Paragraph <= anchor.Paragraph + 1);

                // Ctrl goes to the corner of what shows instead of paging, as in FieldWorks: the end of the last visible line, the start of the first.
                s.Caret(10, 4);
                var visible = s.Field.VisibleBounds;
                s.Press(Key.PageDown, KeyModifiers.Control);
                Assert.False(s.Editor.HasSelection);
                Assert.InRange(s.Field.CaretRect().Bottom, visible.Bottom - 60, visible.Bottom + 40);   // the last line may be cut off by the edge
                s.Press(Key.PageUp, KeyModifiers.Control | KeyModifiers.Shift);
                Assert.True(s.Editor.HasSelection);
                var top = s.Field.CaretRect();
                Assert.True(top.Bottom > visible.Top && top.Y <= visible.Top + 60, "caret " + top + " is on the first line that shows, " + visible);   // that line may be cut off by the edge too

                s.Caret(0, 3);
                s.Press(Key.PageUp);
                Assert.Equal(0, s.Editor.Caret.Paragraph);   // nowhere to go up to: the first line

                s.Press(Key.End, KeyModifiers.Control);
                int last = s.Editor.Document.Paragraphs.Count - 1;
                s.Caret(last - 1, 0);
                for (int i = 0; i < 4; i++) s.Press(Key.PageDown);
                Assert.Equal(last, s.Editor.Caret.Paragraph);   // stops on the last line
            }
        });

        [Fact]
        public void A_page_request_reaches_the_host_with_the_distance_and_a_host_that_handles_it_keeps_the_scrolling() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Long))
            {
                var requests = new List<ScrollRequestedEventArgs>();
                s.Field.ScrollRequested += (o, e) => { e.Handled = true; if (e.Reason == ScrollReason.Page) requests.Add(e); };
                var scroller = ScrollerOf(s);
                s.Caret(10, 4);
                double baseline = scroller.Offset.Y;
                double before = s.Field.CaretRect().Y;
                s.Press(Key.PageDown);
                Assert.Single(requests);
                Assert.Equal(s.Field.CaretRect().Y - before, requests[0].Delta, 3);
                Assert.True(requests[0].Delta > 200);
                Assert.Equal(baseline, scroller.Offset.Y);   // the host handled it and did not scroll
                Assert.Contains("scroll requested: Page by", s.Log);
            }
        });

        [Fact]
        public void A_host_that_knows_its_view_height_sets_the_page_height() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Long))
            {
                s.Field.PageHeight = 120;
                s.Caret(10, 4);
                double before = s.Field.CaretRect().Y;
                s.Press(Key.PageDown);
                double moved = s.Field.CaretRect().Y - before;
                Assert.InRange(moved, 80, 160);
            }
        });

        [Fact]
        public void A_page_move_in_a_field_that_fits_goes_to_its_first_or_last_line() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                s.Caret(2, 3);
                s.Press(Key.PageDown);
                Assert.Equal(s.Editor.Document.Paragraphs.Count - 1, s.Editor.Caret.Paragraph);
                s.Press(Key.PageUp);
                Assert.Equal(0, s.Editor.Caret.Paragraph);
            }
        });

        [Fact]
        public void Dragging_past_the_bottom_of_what_shows_scrolls_by_ten_pixels_a_move_near_the_edge_and_thirty_beyond_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Long))
            {
                var scroller = ScrollerOf(s);
                double bottom = s.Field.TranslatePoint(new Point(0, s.Field.VisibleBounds.Bottom), WindowOf(s))!.Value.Y;
                var from = s.PointAt(3, 5);

                double baseline = scroller.Offset.Y;
                var near = new List<ScrollRequestedEventArgs>();
                EventHandler<ScrollRequestedEventArgs> record = (o, e) => { if (e.Reason == ScrollReason.DragPastEdge) near.Add(e); };
                s.Field.ScrollRequested += record;
                s.MouseDrag(from, new Point(from.X, bottom + 10));
                Assert.NotEmpty(near);
                Assert.All(near, e => Assert.Equal(10, e.Delta));
                Assert.Equal(baseline + 10 * near.Count, scroller.Offset.Y, 3);
                // Only to just past the edge is selected, not to wherever the pointer is.
                Assert.True(s.Editor.Caret.Paragraph >= 1);

                near.Clear();
                double offset = scroller.Offset.Y;
                from = s.PointAt(3, 5);
                s.MouseDrag(from, new Point(from.X, bottom + 80));
                Assert.All(near, e => Assert.Equal(30, e.Delta));
                Assert.Equal(offset + 30 * near.Count, scroller.Offset.Y, 3);
                s.Field.ScrollRequested -= record;
                Assert.Contains("scroll requested: DragPastEdge by 30 px", s.Log);
            }
        });

        [Fact]
        public void Dragging_above_the_top_scrolls_up_and_a_drag_inside_asks_for_nothing() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Long))
            {
                var scroller = ScrollerOf(s);
                s.Press(Key.End, KeyModifiers.Control);
                Headless.Pump();
                Assert.True(scroller.Offset.Y > 0);
                double offset = scroller.Offset.Y;
                var drags = new List<ScrollRequestedEventArgs>();
                s.Field.ScrollRequested += (o, e) => { if (e.Reason == ScrollReason.DragPastEdge) drags.Add(e); };

                var visibleTop = s.Field.TranslatePoint(new Point(0, s.Field.VisibleBounds.Top), WindowOf(s))!.Value;
                var inside = new Point(visibleTop.X + 100, visibleTop.Y + 120);
                s.MouseDrag(inside, new Point(inside.X + 30, inside.Y + 40));
                Assert.Empty(drags);
                Assert.Equal(offset, scroller.Offset.Y);

                s.MouseDrag(inside, new Point(inside.X, visibleTop.Y - 5));
                Assert.NotEmpty(drags);
                Assert.All(drags, e => Assert.Equal(-10, e.Delta));
                Assert.True(scroller.Offset.Y < offset);
            }
        });

        [Fact]
        public void The_caret_is_asked_to_be_shown_after_it_moves_far_and_the_scroll_viewer_brings_it_into_view() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Long))
            {
                var scroller = ScrollerOf(s);
                s.Press(Key.End, KeyModifiers.Control);
                Headless.Pump();
                Assert.True(scroller.Offset.Y > 1000);
                var visible = s.Field.VisibleBounds;
                var caret = s.Field.CaretRect();
                Assert.True(caret.Y >= visible.Top - 1 && caret.Bottom <= visible.Bottom + 1, "caret " + caret + " visible " + visible);
                s.Press(Key.Home, KeyModifiers.Control);
                Headless.Pump();
                Assert.Equal(0, scroller.Offset.Y, 3);
            }
        });
    }
}
