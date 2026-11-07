using System.Linq;
using Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A justified paragraph (align 5): every line but the last has the slack of the line shared out among its spaces, so its text
    /// reaches both edges of the text area; the last line stays at the leading edge. The caret, the selection and the mouse follow the
    /// stretched text, on both Avalonia lines.</summary>
    public class JustificationLayoutTests
    {
        const string English = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot a quick, gloved jab to the jaw of his dizzy opponent.";
        const string Arabic = "هذه فقرة عربية طويلة بما يكفي لتلتف على عدة أسطر عندما تكون النافذة ضيقة وتبين أين تقع الهوامش والمسافات البادئة في الاتجاه من اليمين إلى اليسار وهي تنتهي هنا";
        const string MixedLtr = "English words then ابج دهو in the middle and more English after that so the lines wrap, زحط يكل and some more plain text at the end of it.";
        const string MixedRtl = "هذا نص عربي with some English words وكلمات عربية أخرى in the middle of the line and نهاية الفقرة هنا جملة طويلة";

        static ParagraphLayout Layout(string text, bool rtl, int align = 5, double width = 320)
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", text);
            p.Rtl = rtl;
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            return Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
        }

        /// <summary>The left and right edges of the text of a line, without the space a wrapped line ends with.</summary>
        static (double Left, double Right) Ink(ParagraphLayout pl, LineInfo li)
        {
            int end = pl.TextEnd(li);
            while (end > li.Start && pl.EffectiveText[end - 1] == ' ') end--;
            var rects = pl.SelectionRects(pl.ToModel(li.Start), pl.ToModel(end));
            return (rects.Min(r => r.X), rects.Max(r => r.Right));
        }

        public static readonly object[][] Cases =
        {
            new object[] { English, false },
            new object[] { Arabic, true },
            new object[] { MixedLtr, false },
            new object[] { MixedRtl, true },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void Lines_but_the_last_reach_both_edges_of_the_text_area_and_the_last_stays_at_the_leading_edge(string text, bool rtl) => Headless.Run(() =>
        {
            var pl = Layout(text, rtl);
            Assert.True(pl.Lines.Count >= 3, "lines: " + pl.Lines.Count);
            double left = pl.ContentLeft, right = pl.ContentLeft + pl.ContentWidth;
            for (int i = 0; i < pl.Lines.Count - 1; i++)
            {
                var ink = Ink(pl, pl.Lines[i]);
                Assert.True(System.Math.Abs(ink.Left - left) < 1, "line " + i + " starts at " + ink.Left + ", the area at " + left);
                Assert.True(System.Math.Abs(ink.Right - right) < 1, "line " + i + " ends at " + ink.Right + ", the area at " + right);
            }
            var last = Ink(pl, pl.Lines[pl.Lines.Count - 1]);
            if (rtl)
            {
                Assert.True(System.Math.Abs(last.Right - right) < 1, "the last line starts at the right edge: " + last.Right);
                Assert.True(last.Left > left + 10, "and is not stretched to the left edge: " + last.Left);
            }
            else
            {
                Assert.True(System.Math.Abs(last.Left - left) < 1, "the last line starts at the left edge: " + last.Left);
                Assert.True(last.Right < right - 10, "and is not stretched to the right edge: " + last.Right);
            }
        });

        [Theory]
        [MemberData(nameof(Cases))]
        public void The_caret_and_the_mouse_follow_the_stretched_text(string text, bool rtl) => Headless.Run(() =>
        {
            var pl = Layout(text, rtl);
            foreach (var li in pl.Lines)
            {
                foreach (var stop in pl.VisualStops(li))
                {
                    int model = pl.ToModel(stop.Offset);
                    if (stop.X > pl.LayoutWidth - ParagraphLayout.CaretWidth - 0.01) continue;   // held inside the control, so not where the character edge is
                    // Clicking on a stop puts the caret where the stop is (at a boundary between directions two places share an offset).
                    var hit = pl.HitTestDetailed(new Point(stop.X, li.Y + 2));
                    Assert.True(System.Math.Abs(pl.CaretRect(hit.Offset, false, hit.CaretHit).X - stop.X) < 1, "a click at " + stop.X + " gave offset " + hit.Offset + " for the stop at offset " + stop.Offset);
                    var caret = pl.CaretRect(model, false, stop.Hit);
                    Assert.True(System.Math.Abs(caret.X - stop.X) < 1, "the caret at " + model + " is at " + caret.X + ", its stop at " + stop.X);
                }
                // Stops run in order and stay inside the text area (apart from the space a wrapped line ends with).
                var xs = pl.VisualStops(li).Select(s => s.X).ToList();
                for (int k = 1; k < xs.Count; k++) Assert.True(xs[k] >= xs[k - 1]);
            }
        });

        [Theory]
        [MemberData(nameof(Cases))]
        public void A_selection_across_a_stretched_line_covers_the_stretched_text_and_no_more(string text, bool rtl) => Headless.Run(() =>
        {
            var pl = Layout(text, rtl);
            var li = pl.Lines[0];
            int end = pl.TextEnd(li);
            while (pl.EffectiveText[end - 1] == ' ') end--;
            var rects = pl.SelectionRects(0, end);
            double total = rects.Sum(r => r.Width);
            Assert.True(System.Math.Abs(total - pl.ContentWidth) < 1.5, "the selection of the line's text is " + total + " wide, the area " + pl.ContentWidth);
            // The spaces are stretched, the words are not.
            int word = text.IndexOf(' ');
            var inner = pl.SelectionRects(0, word);
            var natural = Layout(text, rtl, align: 0).SelectionRects(0, word);
            Assert.Equal(natural.Sum(r => r.Width), inner.Sum(r => r.Width), 1);   // inside a word nothing is stretched
        });

        [Fact]
        public void Other_alignments_and_a_single_line_are_not_stretched() => Headless.Run(() =>
        {
            foreach (int align in new[] { 0, 1, 2, 3, 4 })
            {
                var pl = Layout(English, false, align);
                var natural = Ink(pl, pl.Lines[0]);
                Assert.Equal(pl.Lines[0].Line.Width, natural.Right - natural.Left, 1);   // the text is as wide as Avalonia laid it out
            }
            var single = Layout("one short line", false);
            Assert.Single(single.Lines);
            var ink = Ink(single, single.Lines[0]);
            Assert.True(ink.Right < 200, "a single line is the last line: " + ink.Right);
        });

        static RichTextField Field(Paragraph p, double width = 320)
        {
            p.Props.SetInt(TextPropertyNumbers.Align, 5);
            return Fixtures.Field(Doc(p), width);
        }

        static void AssertReachesBothEdges(ParagraphLayout pl, LineInfo li, string what)
        {
            var ink = Ink(pl, li);
            double left = pl.ContentLeft, right = pl.ContentLeft + pl.ContentWidth;
            Assert.True(System.Math.Abs(ink.Left - left) < 1.5, what + " starts at " + ink.Left + ", the area at " + left);
            Assert.True(System.Math.Abs(ink.Right - right) < 1.5, what + " ends at " + ink.Right + ", the area at " + right);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_line_that_ends_at_a_hard_line_break_is_stretched_like_any_line_but_the_last(bool rtl) => Headless.Run(() =>
        {
            string text = rtl
                ? "هذا سطر قصير\u2028" + Arabic + "\u2028سطر أخير"
                : "boxer shot a quick\u2028jab to the jaw of his dizzy opponent while the five boxing wizards jump quickly and the lazy dog sleeps\u2028the last line";
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", text);
            p.Rtl = rtl;
            var pl = Field(p).ParagraphLayoutFor(0);
            int first = text.IndexOf('\u2028'), second = text.LastIndexOf('\u2028');
            Assert.True(pl.Lines.Count >= 4, "lines: " + pl.Lines.Count);
            AssertReachesBothEdges(pl, pl.Lines[0], "the short line before the first break");
            Assert.Equal(first, pl.TextEnd(pl.Lines[0]));
            var lineBeforeSecond = pl.Lines[pl.LineIndexOf(second - 1)];
            AssertReachesBothEdges(pl, lineBeforeSecond, "the last line before the second break");
            var last = Ink(pl, pl.Lines[pl.Lines.Count - 1]);
            double edge = rtl ? pl.ContentLeft : pl.ContentLeft + pl.ContentWidth;
            Assert.True(System.Math.Abs((rtl ? last.Left : last.Right) - edge) > 20, "the paragraph's last line is not stretched: " + last);
            // A click on each stop of the stretched line before a break finds the stop.
            foreach (var stop in pl.VisualStops(pl.Lines[0]))
            {
                var hit = pl.HitTestDetailed(new Point(stop.X, pl.Lines[0].Y + 2));
                Assert.True(System.Math.Abs(pl.CaretRect(hit.Offset, false, hit.CaretHit).X - stop.X) < 1);
            }
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void With_a_line_limit_the_stretched_lines_reach_both_edges_and_the_ellipsis_follows_the_text_of_the_last_one(bool rtl) => Headless.Run(() =>
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", rtl ? Arabic : English);
            p.Rtl = rtl;
            p.Props.SetInt(TextPropertyNumbers.MaxLines, 3);
            var pl = Field(p).ParagraphLayoutFor(0);
            Assert.Equal(3, pl.Lines.Count);
            Assert.True(pl.HasEllipsis);
            AssertReachesBothEdges(pl, pl.Lines[0], "line 0");
            AssertReachesBothEdges(pl, pl.Lines[1], "line 1");
            // The last visible line is stretched to what is left once the ellipsis has its room, and the ellipsis follows it, so the two fill the area.
            var dots = pl.EllipsisRect;
            var ink = Ink(pl, pl.Lines[2]);
            if (rtl)
            {
                Assert.True(System.Math.Abs(ink.Right - (pl.ContentLeft + pl.ContentWidth)) < 1.5, "the last line starts at the right edge: " + ink.Right);
                Assert.True(System.Math.Abs(dots.X - pl.ContentLeft) < 2, "the ellipsis ends at the left edge: " + dots);
                Assert.True(dots.Right <= ink.Left + 8, "and is left of the text: " + dots + " against " + ink.Left);
            }
            else
            {
                Assert.True(System.Math.Abs(ink.Left - pl.ContentLeft) < 1.5, "the last line starts at the left edge: " + ink.Left);
                Assert.True(System.Math.Abs(dots.Right - (pl.ContentLeft + pl.ContentWidth)) < 2, "the ellipsis ends at the right edge: " + dots);
                Assert.True(dots.X >= ink.Right - 8, "and is right of the text: " + dots + " against " + ink.Right);
            }
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_bullet_or_number_stays_put_and_the_text_beside_it_is_stretched_to_the_edges(bool rtl) => Headless.Run(() =>
        {
            Paragraph Build(int align)
            {
                var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", rtl ? Arabic : English);
                p.Rtl = rtl;
                p.Props.SetInt(TextPropertyNumbers.LeadingIndent, 36000, TextPropertyNumbers.VariationMilliPoint);
                p.Props.SetInt(TextPropertyNumbers.FirstIndent, -24000, TextPropertyNumbers.VariationMilliPoint);
                p.Props.SetInt(TextPropertyNumbers.BulNumScheme, 10);
                p.Props.SetStr(TextPropertyNumbers.BulNumTxtBefore, "Chapter number ");
                p.Props.SetStr(TextPropertyNumbers.BulNumTxtAfter, ".");
                p.Props.SetInt(TextPropertyNumbers.Align, align);
                return p;
            }
            var plain = Fixtures.Field(Doc(Build(0)), 330).ParagraphLayoutFor(0);
            var pl = Fixtures.Field(Doc(Build(5)), 330).ParagraphLayoutFor(0);
            Assert.True(pl.Lines.Count >= 4, "lines: " + pl.Lines.Count);
            Assert.Equal(plain.LabelRect.X, pl.LabelRect.X, 2);
            Assert.Equal(plain.LabelRect.Y, pl.LabelRect.Y, 2);
            Assert.Equal(plain.LabelRect.Width, pl.LabelRect.Width, 2);
            // The first line starts after the label (the label is wider than the hanging indent); every line ends at the trailing edge, and the
            // lines after the first also start at the leading one.
            var first = Ink(pl, pl.Lines[0]);
            double left = pl.ContentLeft, right = pl.ContentLeft + pl.ContentWidth;
            if (rtl)
            {
                Assert.True(System.Math.Abs(first.Left - left) < 1.5, "the first line ends at the left edge: " + first.Left);
                Assert.True(first.Right < pl.LabelRect.X + 1, "and starts left of the label: " + first.Right + " against " + pl.LabelRect.X);
            }
            else
            {
                Assert.True(System.Math.Abs(first.Right - right) < 1.5, "the first line ends at the right edge: " + first.Right);
                Assert.True(first.Left > pl.LabelRect.Right - 1, "and starts right of the label: " + first.Left + " against " + pl.LabelRect.Right);
            }
            for (int i = 1; i < pl.Lines.Count - 1; i++) AssertReachesBothEdges(pl, pl.Lines[i], "line " + i);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_change_of_formatting_right_after_a_space_does_not_make_the_stretched_line_overshoot_the_edge(bool rtl) => Headless.Run(() =>
        {
            var p = rtl
                ? Para("ar", "ar", "هذه فقرة عربية طويلة بما يكفي لتلتف على ", "ar", "عدة أسطر عندما تكون النافذة ضيقة وتبين أين ", "ar", "تقع الهوامش والمسافات البادئة في الاتجاه من اليمين إلى اليسار وهي تنتهي هنا")
                : Para("en", "en", "The quick brown fox ", "en", "jumps over the lazy dog while the five boxing wizards ", "en", "jump quickly and a mad boxer shot a quick, gloved jab to the jaw of his dizzy opponent.");
            p.Rtl = rtl;
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Italic, TextPropertyNumbers.ToggleOn, 0);
            var pl = Field(p).ParagraphLayoutFor(0);
            Assert.True(pl.Lines.Count >= 3, "lines: " + pl.Lines.Count);
            for (int i = 0; i < pl.Lines.Count - 1; i++) AssertReachesBothEdges(pl, pl.Lines[i], "line " + i);
        });

        [Fact]
        public void A_run_background_is_unbroken_across_the_stretched_spaces_of_a_justified_line() => Headless.Run(() =>
        {
            var p = Para("en", "en", "The quick brown ", "en", "fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot ", "en", "a quick, gloved jab to the jaw of his dizzy opponent.");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.BackColor, 0x80FFFF, 0);
            var field = Field(p);
            var pl = field.ParagraphLayoutFor(0);
            var shot = Fixtures.Render(field);
            int start = 16, end = 16 + p.Runs[1].Text.Length, wholeLines = 0;
            for (int l = 0; l < pl.Lines.Count - 1; l++)
            {
                var li = pl.Lines[l];
                if (li.Start < start || pl.TextEnd(li) > end) continue;
                wholeLines++;
                var ink = Ink(pl, li);
                double y = field.ParagraphTop(0) + li.TextY + 1;
                for (int x = (int)System.Math.Ceiling(ink.Left) + 1; x < (int)System.Math.Floor(ink.Right) - 1; x++)
                    Assert.True(shot.HasColor(x, (int)y), "line " + l + ": no background at x " + x);
            }
            Assert.True(wholeLines >= 1, "a line inside the coloured run was checked");
        });

        [Fact]
        public void A_run_with_a_baseline_offset_is_raised_on_a_stretched_line_with_its_text_and_selection_in_step() => Headless.Run(() =>
        {
            var p = Para("en", "en", "The quick brown fox ", "en", "jumps over the lazy dog while the five boxing wizards jump", "en", " quickly and a mad boxer shot a quick, gloved jab to the jaw.");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Offset, 6000, TextPropertyNumbers.VariationMilliPoint);
            var field = Field(p);
            var pl = field.ParagraphLayoutFor(0);
            var shot = Fixtures.Render(field);
            int start = p.Runs[0].Text.Length, end = start + p.Runs[1].Text.Length;
            var li = pl.Lines[0];
            Assert.True(li.IsJustified && li.Raise > 5.9, "the first line is stretched and makes room for the raised run");
            // Each selection box is where its text is drawn, raised or not.
            var plain = Assert.Single(pl.SelectionRects(0, start - 1));
            var raised = pl.SelectionRects(start, System.Math.Min(end, pl.TextEnd(li)));
            Assert.All(raised, r => Assert.Equal(li.TextY - 6, r.Y, 2));
            Assert.Equal(li.TextY, plain.Y, 2);
            double top = field.ParagraphTop(0) + li.Y;
            int plainTop = shot.InkTop(plain.X, plain.Right, top, top + li.Height);
            int raisedTop = shot.InkTop(raised.Min(r => r.X), raised.Max(r => r.Right), top, top + li.Height);
            Assert.InRange(plainTop - raisedTop, 5, 7);   // six pixels higher
            AssertReachesBothEdges(pl, li, "the line with the raised run");
        });

        const string Overhang = "a fox jumps over a jolly jug, just a jiffy joke: jam, jazz, jets and jugs, a jab, a jig, jewels, a fjord, jaws, a jaunty jaguar, joyful jays";

        /// <summary>Counts the interior gaps of the justified lines (stretched spaces at least 8 pixels wide) and those with a smear in their
        /// middle: a row of ink three pixels long or more. The ink of the letters beside a gap stays with them, unstretched, so the middle of a
        /// stretched space holds none (at most a stray pixel of an italic's slant).</summary>
        static (int Gaps, int Smeared) GapsWithInk(string text, bool italic, double width)
        {
            var p = Para("en", "en", text);
            if (italic) p.Runs[0].Props.SetInt(TextPropertyNumbers.Italic, TextPropertyNumbers.ToggleOn, 0);
            p.Props.SetInt(TextPropertyNumbers.Align, 5);
            var field = Fixtures.Field(Doc(p), width);
            var pl = field.ParagraphLayoutFor(0);
            var shot = Fixtures.Render(field);
            int gaps = 0, smeared = 0;
            for (int l = 0; l < pl.Lines.Count - 1; l++)
            {
                var li = pl.Lines[l];
                double top = field.ParagraphTop(0) + li.TextY;
                int first = li.Start, last = pl.TextEnd(li);
                while (first < last && pl.EffectiveText[first] == ' ') first++;
                while (last > first && pl.EffectiveText[last - 1] == ' ') last--;
                for (int i = first; i < last; i++)
                {
                    if (pl.EffectiveText[i] != ' ') continue;
                    var rects = pl.SelectionRects(i, i + 1);
                    double x0 = rects.Min(r => r.X), x1 = rects.Max(r => r.Right);
                    if (x1 - x0 < 8) continue;
                    gaps++;
                    bool smear = false;
                    for (int y = (int)top; y < (int)System.Math.Ceiling(top + li.Line.Height) && !smear; y++)
                    {
                        int run = 0;
                        for (int x = (int)System.Math.Ceiling(x0) + 2; x < (int)System.Math.Floor(x1) - 2; x++)
                        {
                            run = shot.IsInk(x, y, 200) ? run + 1 : 0;
                            if (run >= 3) smear = true;
                        }
                    }
                    if (smear) smeared++;
                }
            }
            return (gaps, smeared);
        }

        [Theory]
        [InlineData(false, 140)]
        [InlineData(false, 190)]
        [InlineData(true, 140)]
        [InlineData(true, 190)]
        public void The_overhang_of_a_letter_beside_a_stretched_space_stays_with_the_letter_and_is_not_smeared_across_the_gap(bool italic, int width) => Headless.Run(() =>
        {
            var (gaps, smeared) = GapsWithInk(Overhang, italic, width);
            Assert.True(gaps >= 3, "stretched gaps looked at: " + gaps);
            Assert.True(smeared == 0, smeared + " of " + gaps + " stretched spaces have a smear of ink in their middle");
        });
    }
}
