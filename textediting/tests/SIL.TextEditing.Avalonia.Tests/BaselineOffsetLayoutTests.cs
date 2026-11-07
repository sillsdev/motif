using System.Linq;
using Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A run's baseline offset (raised or lowered, in points drawn as pixels): the line makes room for it, the caret, the selection
    /// and the squiggle follow it, and the lines and offsets of everything else stay where they were.</summary>
    public class BaselineOffsetLayoutTests
    {
        static RichTextField Field(int offsetMilliPoints, bool rtl = false)
        {
            var p = Para("en", "en", "ab", "en", "cd", "en", "ef");
            p.Rtl = rtl;
            if (offsetMilliPoints != 0) p.Runs[1].Props.SetInt(TextPropertyNumbers.Offset, offsetMilliPoints, TextPropertyNumbers.VariationMilliPoint);
            return Fixtures.Field(Doc(p), 520);
        }

        static ParagraphLayout Layout(int offset, bool rtl = false) => Field(offset, rtl).ParagraphLayoutFor(0);

        [Fact]
        public void A_raised_run_makes_the_line_taller_above_and_a_lowered_run_makes_it_taller_below() => Headless.Run(() =>
        {
            var plain = Layout(0).Lines[0];
            var raised = Layout(4000).Lines[0];
            var lowered = Layout(-4000).Lines[0];
            Assert.Equal(plain.Height + 4, raised.Height, 2);
            Assert.Equal(plain.Height + 4, lowered.Height, 2);
            Assert.Equal(plain.Line.Height, raised.Line.Height, 2);   // Avalonia's line is not changed, the slot is
            Assert.Equal(raised.Y + 4, raised.TextY, 2);               // the unshifted text sits 4 below the slot's top
            Assert.Equal(lowered.Y, lowered.TextY, 2);                 // and with a lowered run the room is at the bottom
            // A paragraph below is pushed down by the extra room, not overlapped.
            Assert.Equal(Layout(0).Height + 4, Layout(4000).Height, 2);
        });

        [Fact]
        public void The_caret_inside_or_after_a_raised_run_is_raised_and_before_it_is_not() => Headless.Run(() =>
        {
            var pl = Layout(4000);
            double top = pl.Lines[0].TextY;
            Assert.Equal(top, pl.CaretRect(1).Y, 2);       // between a and b: plain
            Assert.Equal(top, pl.CaretRect(2).Y, 2);       // at the start of the raised run, the caret follows the character before it
            Assert.Equal(top - 4, pl.CaretRect(3).Y, 2);   // inside the raised run
            Assert.Equal(top - 4, pl.CaretRect(4).Y, 2);   // at its end, still raised (typing there continues it)
            Assert.Equal(top, pl.CaretRect(5).Y, 2);
            var lowered = Layout(-4000);
            Assert.Equal(lowered.Lines[0].TextY + 4, lowered.CaretRect(3).Y, 2);
            Assert.Equal(pl.CaretRect(3).Height, pl.CaretRect(1).Height, 2);
        });

        [Fact]
        public void The_selection_of_a_raised_run_is_raised_with_it_and_the_rest_is_not() => Headless.Run(() =>
        {
            var pl = Layout(4000);
            var rects = pl.SelectionRects(0, 6).OrderBy(r => r.X).ToList();
            double top = pl.Lines[0].TextY;
            Assert.Contains(rects, r => System.Math.Abs(r.Y - top) < 0.01);
            Assert.Contains(rects, r => System.Math.Abs(r.Y - (top - 4)) < 0.01);
            var only = Assert.Single(pl.SelectionRects(2, 4));
            Assert.Equal(top - 4, only.Y, 2);
            Assert.Equal(pl.CaretRect(2, true).X, only.X, 1);
            Assert.Equal(pl.CaretRect(4, true).X, only.Right, 1);
            var low = Assert.Single(Layout(-4000).SelectionRects(2, 4));
            Assert.Equal(Layout(-4000).Lines[0].TextY + 4, low.Y, 2);
            // The plain runs on both sides keep the line's own text box.
            var before = Assert.Single(pl.SelectionRects(0, 2));
            Assert.Equal(top, before.Y, 2);
        });

        [Fact]
        public void Hit_testing_and_vertical_places_ignore_the_offset_so_the_mouse_finds_the_same_characters() => Headless.Run(() =>
        {
            var pl = Layout(4000);
            var plain = Layout(0);
            foreach (int offset in new[] { 0, 1, 2, 3, 4, 5, 6 })
            {
                double x = plain.CaretRect(offset).X;
                var raisedX = pl.CaretRect(offset).X;
                Assert.Equal(x, raisedX, 2);   // the offset moves the run up, not along
                Assert.Equal(offset, pl.HitTest(new Point(raisedX, pl.Lines[0].Y + 2)));
                Assert.Equal(offset, pl.HitTest(new Point(raisedX, pl.Lines[0].Y + pl.Lines[0].Height - 2)));
            }
        });

        [Fact]
        public void A_right_to_left_paragraph_raises_the_same_way() => Headless.Run(() =>
        {
            var pl = Layout(4000, rtl: true);
            Assert.Equal(Layout(0, rtl: true).Lines[0].Height + 4, pl.Lines[0].Height, 2);
            Assert.Equal(pl.Lines[0].TextY - 4, pl.CaretRect(3).Y, 2);
            Assert.Equal(pl.Lines[0].TextY - 4, Assert.Single(pl.SelectionRects(2, 4)).Y, 2);
        });

        const string ShortRun = "The quick brown ";
        const string LongRun = "fox jumps over the lazy dog and the five boxing wizards jump quickly";

        /// <summary>A right-to-left paragraph whose Latin runs differ in length, the second (longer) one raised by 6 points. Avalonia numbers the
        /// runs of a line in visual order there, so a run is found by where it sits, not by that number.</summary>
        static RichTextField UnequalRuns(bool arabicAround, double width = 300)
        {
            var p = arabicAround
                ? Para("ar", "ar", "مرحبا بالعالم ", "en", ShortRun, "en", LongRun, "ar", " هنا نهاية الفقرة العربية")
                : Para("ar", "en", ShortRun, "en", LongRun, "en", " tail");
            p.Rtl = true;
            p.Runs[arabicAround ? 2 : 1].Props.SetInt(TextPropertyNumbers.Offset, 6000, TextPropertyNumbers.VariationMilliPoint);
            return Fixtures.Field(Doc(p), width);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void In_a_right_to_left_paragraph_the_run_with_the_offset_is_raised_when_the_latin_runs_differ_in_length(bool arabicAround) => Headless.Run(() =>
        {
            var pl = UnequalRuns(arabicAround).ParagraphLayoutFor(0);
            int start = arabicAround ? 14 + ShortRun.Length : ShortRun.Length, end = start + LongRun.Length;
            Assert.True(pl.Lines.Count >= 3, "lines: " + pl.Lines.Count);
            foreach (var li in pl.Lines)
            {
                bool hasRaised = li.Start < end && pl.TextEnd(li) > start;
                Assert.Equal(hasRaised ? 6 : 0, li.Raise, 2);   // the slot makes room for the raised run, and only on lines that have it
                foreach (var r in pl.SelectionRects(li.Start, pl.TextEnd(li)))
                    Assert.True(r.Y >= li.Y - 0.01 && r.Bottom <= li.Y + li.Height + 0.01, "a selection box stays inside its line's slot");
                int a = System.Math.Max(start, li.Start), b = System.Math.Min(end, pl.TextEnd(li));
                if (b > a)
                    foreach (var r in pl.SelectionRects(a, b))
                        Assert.Equal(li.TextY - 6, r.Y, 2);   // the raised run, wherever it wraps
                int c = System.Math.Max(arabicAround ? start - ShortRun.Length : 0, li.Start), d = System.Math.Min(start, pl.TextEnd(li));
                if (d > c)
                    foreach (var r in pl.SelectionRects(c, d))
                        Assert.Equal(li.TextY, r.Y, 2);       // the short run beside it is not
            }
            Assert.Equal(pl.Lines[0].TextY - 6, pl.CaretRect(start + 3).Y, 2);
            Assert.Equal(pl.Lines[0].TextY, pl.CaretRect(start - 4).Y, 2);
        });

        [Fact]
        public void In_a_right_to_left_paragraph_the_run_with_the_offset_is_drawn_raised_and_its_neighbour_is_not() => Headless.Run(() =>
        {
            var field = UnequalRuns(false);
            var pl = field.ParagraphLayoutFor(0);
            var shot = Fixtures.Render(field);
            var li = pl.Lines[0];
            double top = field.ParagraphTop(0) + li.Y;
            var shortRun = Assert.Single(pl.SelectionRects(0, ShortRun.Length));
            var longRun = Assert.Single(pl.SelectionRects(ShortRun.Length, li.End - 1));   // without the space the line wraps on
            // Both pieces have tall letters (T, h, k, b and f, h, l, d): the long run's ink starts 6 rows above the short run's.
            int shortTop = shot.InkTop(shortRun.X, shortRun.Right, top, top + li.Height);
            int longTop = shot.InkTop(longRun.X, longRun.Right, top, top + li.Height);
            Assert.True(shortTop >= 0 && longTop >= 0, "both runs have ink inside their selection boxes");
            Assert.InRange(shortTop - longTop, 5, 7);
            // The selection of the short run is over its own text: it starts where the ink of the line starts, and the long run's box follows it.
            int firstInk = Enumerable.Range(0, shot.Width).First(x => shot.ColumnHasInk(x, top, top + li.Height));
            Assert.InRange(shortRun.X, firstInk - 2, firstInk + 1);
            Assert.True(shortRun.Right <= longRun.X + 0.5 && shortRun.Right > longRun.X - 6, "the long run's box starts where the short one ends");
        });
    }
}
