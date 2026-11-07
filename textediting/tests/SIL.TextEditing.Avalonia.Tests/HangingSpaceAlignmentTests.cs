using System;
using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>The spaces a wrapped line ends with hang outside its text, so a line is aligned by its text: the visible text of a right-aligned line
    /// ends at the right edge of the text area, that of a left-aligned one starts at the left edge and that of a centred one is centred, in either
    /// direction, whatever room the spaces need. As in FieldWorks, which leaves them out of the width a line is aligned by.</summary>
    public class HangingSpaceAlignmentTests
    {
        const string English = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot a quick, gloved jab to the jaw of his dizzy opponent.";
        const string Arabic = "هذه فقرة عربية طويلة بما يكفي لتلتف على عدة أسطر عندما تكون النافذة ضيقة وتبين أين تقع الهوامش والمسافات البادئة في الاتجاه من اليمين إلى اليسار وهي تنتهي هنا";

        static string Spaced(string text, int spaces) => string.Join(new string(' ', spaces), text.Split(' '));

        static ParagraphLayout Layout(string text, bool rtl, int align, double width, int spaces = 1)
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", Spaced(text, spaces));
            p.Rtl = rtl;
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            return Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
        }

        /// <summary>Where a wrapped line's text, without the spaces it ends with, is: its left and right edges in paragraph coordinates.</summary>
        internal static (double Left, double Right) Ink(ParagraphLayout pl, LineInfo li)
        {
            int core = pl.TextEnd(li);
            while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
            var rects = pl.SelectionRects(li.Start, core);
            return (rects.Min(r => r.X), rects.Max(r => r.Right));
        }

        public static readonly object[][] Cases =
        {
            new object[] { English, false, 1 },
            new object[] { English, false, 2 },
            new object[] { English, false, 3 },
            new object[] { English, false, 4 },
            new object[] { Arabic, true, 1 },
            new object[] { Arabic, true, 2 },
            new object[] { Arabic, true, 3 },
            new object[] { Arabic, true, 4 },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void A_wrapped_line_is_aligned_by_its_text_not_by_the_spaces_it_ends_with(string text, bool rtl, int align) => Headless.Run(() =>
        {
            // Where the alignment goes: Left and Right as they say, Trailing at the end of the paragraph's direction.
            bool left = align == 1 || (align == 4 && rtl);
            bool centre = align == 2;
            int checkedLines = 0;
            foreach (int spaces in new[] { 1, 3 })
                for (double width = 200; width <= 260; width += 7)
                {
                    var pl = Layout(text, rtl, align, width, spaces);
                    double leftEdge = pl.ContentLeft, rightEdge = pl.ContentLeft + pl.ContentWidth;
                    for (int i = 0; i < pl.Lines.Count - 1; i++)
                    {
                        var li = pl.Lines[i];
                        if (li.Line.Width > pl.ContentWidth) continue;
                        var (a, b) = Ink(pl, li);
                        string where = "width " + width + ", " + spaces + " spaces, line " + i + ": text " + a + " to " + b + " in " + leftEdge + " to " + rightEdge;
                        if (centre) Assert.True(Math.Abs((a + b) / 2 - (leftEdge + rightEdge) / 2) < 0.75, where);
                        else if (left) Assert.True(Math.Abs(a - leftEdge) < 0.75, where);
                        else Assert.True(Math.Abs(b - rightEdge) < 0.75, where);
                        checkedLines++;
                    }
                }
            Assert.True(checkedLines > 30, "lines checked: " + checkedLines);
        });


        public static readonly object[][] AllCases =
        {
            new object[] { English, false, 0 },
            new object[] { English, false, 1 },
            new object[] { English, false, 2 },
            new object[] { English, false, 3 },
            new object[] { English, false, 4 },
            new object[] { Arabic, true, 0 },
            new object[] { Arabic, true, 1 },
            new object[] { Arabic, true, 2 },
            new object[] { Arabic, true, 3 },
            new object[] { Arabic, true, 4 },
        };

        [Theory]
        [MemberData(nameof(AllCases))]
        public void A_line_whose_text_fits_but_whose_spaces_do_not_is_still_aligned_by_its_text_inside_the_area(string text, bool rtl, int align) => Headless.Run(() =>
        {
            // Avalonia keeps such a line against the leading edge, so its text, and not only its spaces, reaches past the edge on the other side.
            bool left = align == 1 || (align == 4 && rtl) || ((align == 0 || align == 5) && !rtl);
            bool centre = align == 2;
            int checkedLines = 0;
            for (double width = 200; width <= 300; width += 1)
            {
                var pl = Layout(text, rtl, align, width);
                double leftEdge = pl.ContentLeft, rightEdge = pl.ContentLeft + pl.ContentWidth;
                for (int i = 0; i < pl.Lines.Count - 1; i++)
                {
                    var li = pl.Lines[i];
                    if (li.Line.Width > pl.ContentWidth || li.Line.WidthIncludingTrailingWhitespace <= pl.ContentWidth) continue;
                    var (a, b) = Ink(pl, li);
                    string where = "width " + width + ", line " + i + ": text " + a + " to " + b + " in " + leftEdge + " to " + rightEdge;
                    Assert.True(a >= leftEdge - 0.75 && b <= rightEdge + 0.75, where);
                    if (centre) Assert.True(Math.Abs((a + b) / 2 - (leftEdge + rightEdge) / 2) < 0.75, where);
                    else if (left) Assert.True(Math.Abs(a - leftEdge) < 0.75, where);
                    else Assert.True(Math.Abs(b - rightEdge) < 0.75, where);
                    checkedLines++;
                }
            }
            Assert.True(checkedLines > 5, "lines checked: " + checkedLines);
        });

        [Theory]
        [MemberData(nameof(AllCases))]
        public void No_caret_stop_in_the_text_of_a_line_is_outside_the_text_area_before_the_control_edge_holds_it(string text, bool rtl, int align) => Headless.Run(() =>
        {
            // The End caret of a line with hanging spaces is meant to be past its text (and is then held at the edge of the control); every other
            // place is in the text area as it is. The control's own clamp must not be what puts it there.
            int checkedStops = 0;
            foreach (int spaces in new[] { 1, 3 })
                for (double width = 200; width <= 300; width += 3)
                {
                    var pl = Layout(text, rtl, align, width, spaces);
                    double leftEdge = pl.ContentLeft, rightEdge = pl.ContentLeft + pl.ContentWidth;
                    foreach (var li in pl.Lines)
                    {
                        int core = pl.TextEnd(li);
                        while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
                        for (int o = li.Start; o <= core; o++)
                        {
                            var hits = new System.Collections.Generic.List<global::Avalonia.Media.CharacterHit> { new global::Avalonia.Media.CharacterHit(o) };
                            if (o > li.Start) hits.Add(new global::Avalonia.Media.CharacterHit(o - 1, 1));
                            foreach (var hit in hits)
                            {
                                double x = pl.CaretXBeforeClamp(li, hit);
                                Assert.True(x >= leftEdge - 1 && x <= rightEdge + 1, "width " + width + ", " + spaces + " spaces: the stop at " + o + " is at " + x + ", the text area is " + leftEdge + " to " + rightEdge);
                                checkedStops++;
                            }
                        }
                    }
                }
            Assert.True(checkedStops > 1000, "stops checked: " + checkedStops);
        });

        static ParagraphLayout LayoutRuns(string text, bool rtl, int align, double width, int cut)
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", text.Substring(0, cut), rtl ? "ar" : "en", text.Substring(cut));
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
            p.Rtl = rtl;
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            return Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
        }

        [Theory]
        [InlineData(false, 3)]
        [InlineData(false, 4)]
        [InlineData(true, 1)]
        [InlineData(true, 4)]
        public void A_wrapped_line_of_several_runs_with_right_to_left_text_is_aligned_by_its_text_and_never_pushed_past_the_edge(bool rtl, int align) => Headless.Run(() =>
        {
            // Avalonia's own width and trailing-space length are wrong for such a line, so the hang is measured from the spaces as drawn.
            const string mixed = "Wonderful هذه   طويلة   طويلة fox dog   عربية 12 هذه   ab طويلة   cd";
            int checkedLines = 0;
            foreach (int spaces in new[] { 1, 3 })
            {
                string text = spaces == 1 ? mixed.Replace("   ", " ") : mixed;
                for (int cut = 3; cut < text.Length - 3; cut += 4)
                    for (double width = 120; width <= 260; width += 7)
                    {
                        var pl = LayoutRuns(text, rtl, align, width, cut);
                        for (int i = 0; i < pl.Lines.Count - 1; i++)
                        {
                            var li = pl.Lines[i];
                            if (li.Line.Width > pl.ContentWidth * 2) continue;
                            var (a, b) = Ink(pl, li);
                            if (b - a > pl.ContentWidth) continue;   // a word wider than the area
                            string where = "cut " + cut + ", width " + width + ", " + spaces + " spaces, line " + i + ": text " + a + " to " + b;
                            Assert.True(a >= -0.75 && b <= width + 0.75, where + " is outside the control");
                            if (rtl) Assert.True(Math.Abs(a - pl.ContentLeft) < 0.75, where);
                            else Assert.True(Math.Abs(b - (pl.ContentLeft + pl.ContentWidth)) < 0.75, where);
                            checkedLines++;
                        }
                    }
            }
            Assert.True(checkedLines > 200, "lines checked: " + checkedLines);
        });

        [Fact]
        public void No_wrapped_line_of_random_mixed_runs_is_pushed_past_the_control_edge_or_off_its_aligned_edge() => Headless.Run(() =>
        {
            var words = new[] { "fox", "dog", "Wonderful", "عربي", "طويلة", "12", "(جمل)", "ab", "هذه", "ét" };
            var rnd = new Random(7);
            int lines = 0;
            for (int n = 0; n < 2500; n++)
            {
                var sb = new System.Text.StringBuilder();
                for (int j = rnd.Next(4, 16); j > 0; j--)
                    sb.Append(words[rnd.Next(words.Length)]).Append(new[] { " ", "   ", " ", "  " }[rnd.Next(4)]);
                string text = sb.ToString();
                bool rtl = rnd.Next(2) == 0;
                int align = rtl ? new[] { 1, 4 }[rnd.Next(2)] : new[] { 3, 4 }[rnd.Next(2)];
                double width = 100 + rnd.Next(320);
                var p = Para(rtl ? "ar" : "en");
                for (int pos = 0, runs = rnd.Next(1, 5); pos < text.Length; runs--)
                {
                    int len = runs <= 1 ? text.Length - pos : Math.Min(text.Length - pos, rnd.Next(1, text.Length / 2 + 2));
                    var run = new Run { Ws = rnd.Next(3) == 0 ? "ar" : "en", Text = text.Substring(pos, len) };
                    if (rnd.Next(2) == 0) run.Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
                    p.Runs.Add(run);
                    pos += len;
                }
                p.Rtl = rtl;
                p.Props.SetInt(TextPropertyNumbers.Align, align);
                var pl = Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
                for (int i = 0; i < pl.Lines.Count - 1; i++)
                {
                    var li = pl.Lines[i];
                    if (li.End > li.Start && pl.EffectiveText[li.End - 1] == (char)0x2028) continue;
                    int core = pl.TextEnd(li);
                    while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
                    if (core == li.Start) continue;
                    var (a, b) = Ink(pl, li);
                    if (b - a > pl.ContentWidth) continue;
                    Assert.True(a >= -1 && b <= width + 1, "paragraph " + n + " line " + i + ": text " + a + " to " + b + " in a control " + width + " wide");
                    double edge = rtl ? pl.ContentLeft : pl.ContentLeft + pl.ContentWidth;
                    Assert.True(Math.Abs((rtl ? a : b) - edge) < 1, "paragraph " + n + " line " + i + ": text " + a + " to " + b + ", aligned to " + edge);
                    lines++;
                }
            }
            Assert.True(lines > 3000, "lines checked: " + lines);
        });
        [Fact]
        public void The_text_of_a_right_aligned_wrapped_line_reaches_the_right_edge_in_the_picture() => Headless.Run(() =>
        {
            // Three spaces between the words, so each wrapped line ends with three that hang: a dozen pixels the line used to stop short by.
            var p = Para("en", "en", Spaced(English, 3));
            p.Props.SetInt(TextPropertyNumbers.Align, 3);
            const double width = 330;
            var field = Fixtures.Field(Doc(p), width);
            var pl = field.ParagraphLayoutFor(0);
            Assert.True(pl.Lines.Count > 2);
            var shot = Fixtures.Render(field);
            for (int i = 0; i < pl.Lines.Count - 1; i++)
            {
                var li = pl.Lines[i];
                int right = -1;
                for (int x = shot.Width - 1; x >= 0 && right < 0; x--)
                    if (shot.ColumnHasInk(x, li.TextY + 2, li.TextY + li.Line.Height - 2)) right = x;
                Assert.True(right >= width - 3, "line " + i + ": the last ink is at x " + right + " in a field " + width + " wide");
            }
        });
    }
}
