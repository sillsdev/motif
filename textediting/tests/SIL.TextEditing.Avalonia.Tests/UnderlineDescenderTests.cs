using System;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>An underline is one continuous line under the whole run, through the descenders of g, j, p, q and y: the field draws it itself in every
    /// paragraph, not Avalonia, which leaves a gap at each descender. As in FieldWorks, which draws it as a plain line.</summary>
    public class UnderlineDescenderTests
    {
        const string Descenders = "gjpqy gjpqy gjpqy";

        static (RichTextField Field, ParagraphLayout Layout) Make(int align, int underline = 3, int? underColor = null)
        {
            var p = Para("en", "en", Descenders);
            p.Runs[0].Props.SetInt(TextPropertyNumbers.Underline, underline);
            if (underColor != null) p.Runs[0].Props.SetInt(TextPropertyNumbers.UnderColor, underColor.Value);
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            var field = Fixtures.Field(Doc(p), 400);
            return (field, field.ParagraphLayoutFor(0));
        }

        /// <summary>The most ink any one row has in the columns the text spans (device pixels), and how many columns that is.</summary>
        static (int Ink, int Columns) Densest(RichTextField field, ParagraphLayout pl, double scale)
        {
            var shot = Fixtures.Render(field, scale);
            var li = pl.Lines[0];
            double left = li.X + li.Line.Start, right = left + li.Line.Width;
            int from = (int)Math.Ceiling(left * scale) + 1, to = (int)Math.Floor(right * scale) - 1;
            int best = 0;
            for (int y = (int)(li.Y * scale); y < (int)((li.Y + li.Height) * scale); y++)
            {
                int ink = 0;
                for (int x = from; x <= to; x++) if (shot.IsInk(x, y, 200)) ink++;
                best = Math.Max(best, ink);
            }
            return (best, to - from + 1);
        }

        [Theory]
        [InlineData(0, 1.0)]
        [InlineData(2, 1.0)]
        [InlineData(3, 1.0)]
        [InlineData(5, 1.0)]
        [InlineData(0, 1.5)]
        [InlineData(0, 2.0)]
        public void A_single_underline_is_one_unbroken_line_through_the_descenders(int align, double scale) => Headless.Run(() =>
        {
            var (field, pl) = Make(align);
            var (ink, columns) = Densest(field, pl, scale);
            Assert.True(ink >= columns - 1, "the densest row has ink in " + ink + " of " + columns + " columns");
        });

        [Fact]
        public void A_dotted_underline_with_a_colour_runs_on_under_the_descenders() => Headless.Run(() =>
        {
            // The pattern (2 px on, 2 off) is a line of dots, not a solid one: about half of the columns of the densest row, with the descenders' own ink on top.
            var (field, pl) = Make(0, 1, 0x202020);
            var (ink, columns) = Densest(field, pl, 1);
            Assert.InRange(ink, columns * 0.4, columns * 0.8);
        });
    }
}
