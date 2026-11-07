using System.Linq;
using Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A paragraph cut off by its line limit ends its last visible line with an ellipsis, drawn after the text on the trailing side,
    /// inside the text area, with room kept for it; caret positions and hit testing stay those of the text.</summary>
    public class EllipsisLayoutTests
    {
        const string Long = "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november oscar papa quebec romeo sierra tango uniform victor";
        const string LongArabic = "هذه فقرة عربية طويلة بما يكفي لتلتف على عدة أسطر عندما تكون النافذة ضيقة وتبين أين تقع الهوامش والمسافات البادئة في الاتجاه من اليمين إلى اليسار هذه فقرة عربية طويلة بما يكفي لتلتف";

        static ParagraphLayout Layout(string text, int maxLines, bool rtl = false, int align = 0, double width = 300)
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", text);
            p.Rtl = rtl;
            p.Props.SetInt(TextPropertyNumbers.MaxLines, maxLines);
            if (align != 0) p.Props.SetInt(TextPropertyNumbers.Align, align);
            return Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
        }

        [Fact]
        public void The_last_visible_line_of_a_cut_off_paragraph_has_an_ellipsis_after_its_text_inside_the_text_area() => Headless.Run(() =>
        {
            var pl = Layout(Long, 2);
            Assert.True(pl.Truncated);
            Assert.Equal(2, pl.Lines.Count);
            Assert.True(pl.HasEllipsis);
            var last = pl.Lines[1];
            var dots = pl.EllipsisRect;
            Assert.True(dots.Width > 3 && dots.Height > 5);
            Assert.Equal(last.X + last.Line.Start + last.Line.Width, dots.X, 1);   // right after the text of the line
            Assert.True(dots.Right <= pl.ContentLeft + pl.ContentWidth + 0.01, "inside the text area: " + dots + " in " + pl.ContentWidth);
            Assert.Equal(last.TextY, dots.Y, 1);
            Assert.False(Layout(Long, 0).HasEllipsis);
            Assert.False(Layout(Long, 20).HasEllipsis);
            Assert.False(Layout("short", 1).HasEllipsis);
            // The first line, which is not the last visible one, has none, and has the full width.
            Assert.True(pl.Lines[0].Line.Width > pl.ContentWidth * 0.8);
        });

        [Fact]
        public void The_ellipsis_leaves_the_caret_and_hit_testing_on_the_text() => Headless.Run(() =>
        {
            var pl = Layout(Long, 2);
            var last = pl.Lines[1];
            int visible = pl.VisibleEnd;
            Assert.Equal(pl.TextEnd(last), visible);
            var caret = pl.CaretRect(visible);
            Assert.InRange(caret.X - pl.EllipsisRect.X, 0, 8);   // the caret at the end of the visible text is where the ellipsis starts (or a space before it)
            Assert.Equal(visible, pl.HitTest(new Point(pl.EllipsisRect.X + pl.EllipsisRect.Width / 2, last.Y + 2)));   // a click on the ellipsis is the end of the text
            Assert.Equal(visible, pl.HitTest(new Point(pl.ContentLeft + pl.ContentWidth - 1, last.Y + 2)));
            Assert.Equal(last.Start, pl.HitTest(new Point(pl.ContentLeft, last.Y + 2)));
        });

        [Fact]
        public void A_right_to_left_paragraph_puts_the_ellipsis_on_the_left_inside_the_text_area() => Headless.Run(() =>
        {
            var pl = Layout(LongArabic, 2, rtl: true);
            Assert.True(pl.Truncated);
            Assert.True(pl.HasEllipsis);
            var last = pl.Lines[1];
            var dots = pl.EllipsisRect;
            Assert.True(dots.X >= pl.ContentLeft - 0.01, "inside the text area: " + dots);
            int end = pl.TextEnd(last);
            while (end > last.Start && pl.EffectiveText[end - 1] == ' ') end--;   // the ink, without the space a wrapped line ends with
            var ink = last.Line.GetTextBounds(last.Start, end - last.Start);
            double inkLeft = ink.Min(b => last.X + b.Rectangle.X);
            Assert.True(dots.Right <= inkLeft + 1, "the ellipsis " + dots + " is left of the text, which starts at " + inkLeft);
            Assert.Equal(last.TextY, dots.Y, 1);
            Assert.InRange(dots.Right - pl.CaretRect(pl.VisibleEnd).X, -0.5, 8);   // the caret at the visible end is beside it
        });

        [Fact]
        public void The_ellipsis_is_kept_inside_the_text_area_whatever_the_alignment() => Headless.Run(() =>
        {
            foreach (int align in new[] { 1, 2, 3, 4 })
            {
                var pl = Layout(Long, 2, align: align);
                Assert.True(pl.HasEllipsis, "align " + align);
                var dots = pl.EllipsisRect;
                var last = pl.Lines[1];
                Assert.True(dots.X >= last.X + last.Line.Start + last.Line.Width - 0.01, "after the text, align " + align);
                Assert.True(dots.Right <= pl.ContentLeft + pl.ContentWidth + 0.01, "inside the area, align " + align + ": " + dots);
            }
        });

        static readonly string Break = DocumentEditor.LineSeparator.ToString();

        [Fact]
        public void A_trailing_hard_line_break_alone_is_not_cut_off_text_but_text_after_a_break_is() => Headless.Run(() =>
        {
            var onlyBreak = Layout("ab" + Break, 1);
            Assert.False(onlyBreak.Truncated);
            Assert.True(onlyBreak.HiddenTrailingBreak);
            Assert.False(onlyBreak.HasEllipsis);
            var text = Layout("ab" + Break + "cd", 1);
            Assert.True(text.Truncated);
            Assert.True(text.HasEllipsis);
            Assert.Equal(text.CaretRect(2).X, text.EllipsisRect.X, 1);   // after "ab", where the break is (it takes no room)
        });

        [Fact]
        public void A_paragraph_too_narrow_for_an_ellipsis_beside_text_still_lays_out_and_draws_one() => Headless.Run(() =>
        {
            var pl = Layout(Long, 2, width: 30);
            Assert.True(pl.Truncated);
            Assert.True(pl.Lines.Count == 2);
            Assert.True(pl.HasEllipsis);
        });
    }
}
