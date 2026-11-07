using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Underlines and strikethrough on a justified paragraph: they are drawn by the field across the whole stretched extent of the run,
    /// in the places and the pattern Avalonia draws them on a line that is not stretched, with nothing in the rows beside them.</summary>
    public class JustifiedDecorationTests
    {
        // No letter with a descender, so only the decoration is below the baseline.
        const string Words = "one two ten run now rare mint sun more number near over ten new town nine aim use inner main such notion visual";

        static (RichTextField Field, ParagraphLayout Layout) Make(int underline, int align, double width = 300)
        {
            var p = Para("en", "en", Words);
            p.Runs[0].Props.SetInt(TextPropertyNumbers.Underline, underline);
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            var field = Fixtures.Field(Doc(p), width);
            return (field, field.ParagraphLayoutFor(0));
        }

        /// <summary>The rows (in device pixels) that have ink in the columns of the space after the line's <paramref name="word"/>th word, and
        /// the darkest red level in each.</summary>
        static SortedDictionary<int, int> SpaceRows(RichTextField field, ParagraphLayout pl, Fixtures.Shot shot, double scale, int word)
        {
            int at = 0;
            for (int k = 0; k <= word; k++) at = Words.IndexOf(' ', at) + 1;
            var r = pl.SelectionRects(at - 1, at)[0];
            var rows = new SortedDictionary<int, int>();
            for (int x = (int)System.Math.Ceiling(r.X * scale + 0.7); x <= (int)System.Math.Floor(r.Right * scale - 0.7); x++)
                for (int y = (int)(pl.Lines[0].Y * scale); y < (int)((pl.Lines[0].Y + pl.Lines[0].Height) * scale); y++)
                {
                    var c = shot.Pixel(x, y);
                    if (c.A > 128 && c.R < 250) rows[y] = rows.TryGetValue(y, out int old) ? System.Math.Min(old, c.R) : c.R;
                }
            return rows;
        }

        [Theory]
        [InlineData(1, 1.0)]
        [InlineData(2, 1.0)]
        [InlineData(3, 1.0)]
        [InlineData(4, 1.0)]
        [InlineData(5, 1.0)]
        [InlineData(4, 1.5)]
        [InlineData(1, 1.5)]
        [InlineData(3, 1.5)]
        public void A_decoration_in_a_stretched_space_is_in_the_rows_and_strength_it_has_in_a_space_that_is_not_stretched(int underline, double scale) => Headless.Run(() =>
        {
            var natural = Make(underline, 0);
            var justified = Make(underline, 5);
            Assert.True(justified.Layout.Lines.Count > 2 && justified.Layout.Lines[0].IsJustified);
            var shotNatural = Fixtures.Render(natural.Field, scale);
            var shotJustified = Fixtures.Render(justified.Field, scale);
            // The first line of both has the same words (the same line breaks), so the same rows carry the decoration.
            var expected = SpaceRows(natural.Field, natural.Layout, shotNatural, scale, 1);
            var actual = SpaceRows(justified.Field, justified.Layout, shotJustified, scale, 1);
            Assert.NotEmpty(expected);
            Assert.Equal(expected.Keys, actual.Keys);
            foreach (var row in expected.Keys) Assert.True(System.Math.Abs(expected[row] - actual[row]) <= 24, "row " + row + ": level " + actual[row] + " against " + expected[row]);
        });

        [Theory]
        [InlineData(1, 2.0, 2.0)]   // dotted: 2 on, 2 off
        [InlineData(2, 6.0, 3.0)]   // dashed: 6 on, 3 off
        public void A_dotted_or_dashed_line_runs_on_as_one_pattern_across_the_stretched_spaces(int underline, double on, double off) => Headless.Run(() =>
        {
            var (field, pl) = Make(underline, 5);
            var li = pl.Lines[0];
            Assert.True(li.IsJustified);
            var shot = Fixtures.Render(field);
            double start = pl.SelectionRects(0, 3).Min(r => r.X);
            // The row of the line: the one with the most ink.
            int row = Enumerable.Range((int)(li.TextY + li.Line.Baseline), 4).OrderByDescending(y => Enumerable.Range(0, shot.Width).Count(x => shot.Pixel(x, y).R < 200)).First();
            int wrong = 0, checkedPixels = 0;
            double period = on + off;
            for (int x = (int)start + 1; x < pl.ContentWidth - 1; x++)
            {
                double phase = (x + 0.5 - start) % period;
                if (phase < 0.5 || phase > period - 0.5 || System.Math.Abs(phase - on) < 0.5) continue;   // an anti-aliased edge
                bool shouldBeInk = phase < on;
                bool isInk = shot.Pixel(x, row).R < 200;
                checkedPixels++;
                if (isInk != shouldBeInk) wrong++;
            }
            Assert.True(checkedPixels > 100);
            Assert.True(wrong <= 3, wrong + " of " + checkedPixels + " pixels of the line are not where one continuous pattern from the start of the run puts them");
        });

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public void A_double_underline_leaves_no_ink_beside_its_two_lines_in_a_stretched_space(double scale) => Headless.Run(() =>
        {
            var (field, pl) = Make(4, 5);
            var shot = Fixtures.Render(field, scale);
            for (int word = 0; word < 5; word++)
            {
                var rows = SpaceRows(field, pl, shot, scale, word);
                int first = rows.Keys.First();
                // Two one-pixel lines two pixels apart: at most a few rows in all (anti-aliasing at a fractional position), none beside them.
                Assert.True(rows.Keys.Max() - first <= (int)System.Math.Ceiling(5 * scale), "space " + word + ": ink in rows " + string.Join(",", rows.Keys));
                Assert.True(rows.Count <= (int)System.Math.Ceiling(6 * scale), "space " + word + ": ink in rows " + string.Join(",", rows.Keys));
            }
        });

        [Fact]
        public void A_right_to_left_justified_underline_runs_unbroken_from_edge_to_edge() => Headless.Run(() =>
        {
            var p = Para("ar", "ar", "هذه فقرة عربية طويلة بما يكفي لتلتف على عدة أسطر عندما تكون النافذة ضيقة وتبين أين تقع الهوامش والمسافات البادئة");
            p.Rtl = true;
            p.Runs[0].Props.SetInt(TextPropertyNumbers.Underline, 3);
            p.Props.SetInt(TextPropertyNumbers.Align, 5);
            var field = Fixtures.Field(Doc(p), 300);
            var pl = field.ParagraphLayoutFor(0);
            var li = pl.Lines[0];
            Assert.True(li.IsJustified);
            var shot = Fixtures.Render(field);
            int row = Enumerable.Range((int)(li.TextY + li.Line.Baseline), 4).OrderByDescending(y => Enumerable.Range(0, shot.Width).Count(x => shot.Pixel(x, y).R < 200)).First();
            int inkPixels = Enumerable.Range(0, shot.Width).Count(x => shot.Pixel(x, row).R < 235);
            Assert.True(inkPixels > 0.97 * pl.ContentWidth, "the underline covers " + inkPixels + " of " + pl.ContentWidth + " pixels");
        });
    }
}
