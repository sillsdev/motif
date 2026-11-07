using System;
using System.Collections.Generic;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A bullet or number label and the "..." a line limit ends a paragraph with are underlined like the text: one continuous line the field draws
    /// itself (not Avalonia, which leaves a gap at each descender; ParagraphLayout.DrawDecorations).</summary>
    public class LabelUnderlineTests
    {
        sealed class Underlined : IStyleResolver
        {
            public PropertyBag? Resolve(string styleName, string ws) => styleName == "U" ? new PropertyBag().SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSingle) : null;
        }

        /// <summary>The most ink any one row has in the columns between <paramref name="left"/> and <paramref name="right"/>, and how many columns that is.</summary>
        static (int Ink, int Columns) Densest(RichTextField field, double left, double right, double top, double bottom)
        {
            var shot = Fixtures.Render(field);
            int from = (int)Math.Ceiling(left) + 1, to = (int)Math.Floor(right) - 1, best = 0;
            for (int y = (int)top; y < (int)bottom; y++)
            {
                int ink = 0;
                for (int x = from; x <= to; x++) if (shot.IsInk(x, y, 200)) ink++;
                best = Math.Max(best, ink);
            }
            return (best, to - from + 1);
        }

        [Fact]
        public void The_underline_of_a_label_is_one_unbroken_line_through_the_descenders() => Headless.Run(() =>
        {
            var p = Para("en", "en", "Some text");
            p.Style = "U";
            p.Props.SetInt(TextPropertyNumbers.BulNumScheme, BulletNumbers.LetterLower);
            p.Props.SetInt(TextPropertyNumbers.BulNumStartAt, 7);
            p.Props.SetStr(TextPropertyNumbers.BulNumTxtBefore, "gjpqy ");
            p.Props.SetInt(TextPropertyNumbers.FirstIndent, -60000, 1);
            var field = Fixtures.Field(Doc(p), 400, f => f.StyleResolver = new Underlined());
            var pl = field.ParagraphLayoutFor(0);
            var box = pl.LabelRect;
            Assert.True(box.Width > 20, "the label is " + pl.LabelText + " " + box);
            var (ink, columns) = Densest(field, box.X, box.Right, box.Y, box.Bottom + 4);
            Assert.True(ink >= columns - 1, "the densest row has ink in " + ink + " of " + columns + " columns");
            Assert.Null(pl.LabelProperties!.TextDecorations);   // Avalonia is not given the underline
        });

        [Fact]
        public void The_underline_of_the_ellipsis_is_drawn_by_the_field_and_runs_under_all_of_it() => Headless.Run(() =>
        {
            var p = Para("en", "en", "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november oscar papa quebec romeo sierra tango uniform victor");
            p.Style = "U";
            p.Props.SetInt(TextPropertyNumbers.MaxLines, 1);
            var field = Fixtures.Field(Doc(p), 300, f => f.StyleResolver = new Underlined());
            var pl = field.ParagraphLayoutFor(0);
            Assert.True(pl.HasEllipsis);
            Assert.Null(pl.EllipsisProperties!.TextDecorations);   // Avalonia is not given the underline
            Assert.Equal(TextPropertyNumbers.UnderlineSingle, pl.EllipsisProperties.LineKind);
            var box = pl.EllipsisRect;
            var (ink, columns) = Densest(field, box.X, box.Right, box.Y, box.Bottom + 4);
            Assert.True(ink >= columns - 1, "the densest row has ink in " + ink + " of " + columns + " columns");
        });
    }
}
