using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>End on a line that wraps at spaces: the caret stays at the offset after the spaces (the end of the line, as typing and a
    /// selection see it) and is drawn one space width past the text for each of them, as FieldWorks does, even past the text area; it
    /// stops at the edge of the control so it is always seen.</summary>
    public class LineEndCaretTests
    {
        const string English = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot a quick, gloved jab to the jaw of his dizzy opponent.";
        const string Arabic = "هذه فقرة عربية طويلة بما يكفي لتلتف على عدة أسطر عندما تكون النافذة ضيقة وتبين أين تقع الهوامش والمسافات البادئة في الاتجاه من اليمين إلى اليسار وهي تنتهي هنا";

        static ParagraphLayout Layout(string text, bool rtl, int align, double width)
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", text);
            p.Rtl = rtl;
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            return Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
        }

        public static readonly object[][] Cases =
        {
            new object[] { English, false, 0 },
            new object[] { English, false, 5 },
            new object[] { Arabic, true, 0 },
            new object[] { Arabic, true, 5 },
        };

        /// <summary>Every separator is three spaces, so a line that wraps ends with three of them hanging past its last letter.</summary>
        static string Spaced(string text) => string.Join("   ", text.Split(' '));

        static RichTextField Field(string text, bool rtl, int align, double width, bool room)
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", Spaced(text));
            p.Rtl = rtl;
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            if (room)
            {
                // Room beside the text area, so a caret hanging past it is inside the control.
                p.Props.SetInt(TextPropertyNumbers.LeadingIndent, 80000, 1);
                p.Props.SetInt(TextPropertyNumbers.TrailingIndent, 80000, 1);
            }
            return Fixtures.Field(Doc(p), width);
        }

        /// <summary>The offset a wrapped line's text ends at, less the spaces it ends with.</summary>
        static int Core(ParagraphLayout pl, LineInfo li)
        {
            int core = pl.TextEnd(li);
            while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
            return core;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void The_end_of_a_wrapped_line_is_drawn_one_space_width_past_its_text_for_each_trailing_space(string text, bool rtl, int align) => Headless.Run(() =>
        {
            int beyond = 0;
            for (double width = 280; width <= 340; width += 3)
            {
                var field = Field(text, rtl, align, width, true);
                var pl = field.ParagraphLayoutFor(0);
                double left = pl.ContentLeft, right = pl.ContentLeft + pl.ContentWidth;
                for (int i = 0; i < pl.Lines.Count - 1; i++)
                {
                    var li = pl.Lines[i];
                    int core = Core(pl, li), end = pl.LineEnd(i, out var hit);
                    Assert.Equal(li.End, end);   // the offset is still after the spaces: the end of the line
                    Assert.True(end - core >= 3);
                    double fromCore = pl.CaretRect(core, false, pl.EndHit(core)).X, atEnd = pl.CaretRect(end, false, hit).X;
                    double spaces = li.Line.WidthIncludingTrailingWhitespace - li.Line.Width;
                    Assert.True(Math.Abs(Math.Abs(atEnd - fromCore) - spaces) < 1.5, "width " + width + " line " + i + ": the End caret is " + Math.Abs(atEnd - fromCore) + " from the last letter, the spaces are " + spaces + " wide");
                    if (rtl ? atEnd < left - 1 : atEnd > right + 1) beyond++;
                }
            }
            Assert.True(beyond > 5, "lines whose End caret is past the text area: " + beyond);
        });

        [Theory]
        [MemberData(nameof(Cases))]
        public void Right_and_Left_across_the_spaces_a_wrapped_line_ends_with_move_the_caret_each_time(string text, bool rtl, int align) => Headless.Run(() =>
        {
            int walked = 0;
            for (double width = 280; width <= 340; width += 6)
            {
                var field = Field(text, rtl, align, width, true);
                var pl = field.ParagraphLayoutFor(0);
                for (int i = 0; i < pl.Lines.Count - 1; i++)
                {
                    var li = pl.Lines[i];
                    int core = Core(pl, li), end = pl.LineEnd(i, out _);
                    field.Editor.SetCaret(new TextPosition(0, core));
                    double x = field.CaretRect().X;
                    for (int o = core + 1; o <= end; o++)
                    {
                        field.Press(rtl ? Key.Left : Key.Right);
                        Assert.Equal(o, field.Editor.Caret.Offset);
                        double next = field.CaretRect().X;
                        Assert.True(rtl ? next < x - 1 : next > x + 1, "width " + width + " line " + i + ": the caret did not move across the space at " + (o - 1) + " (" + x + " to " + next + ")");
                        x = next;
                    }
                    walked++;
                }
            }
            Assert.True(walked > 20);
        });


        [Theory]
        [MemberData(nameof(Cases))]
        public void The_arrow_keys_across_the_spaces_a_wrapped_line_ends_with_move_the_caret_visibly_each_time_even_with_no_room_beside_the_text_area(string text, bool rtl, int align) => Headless.Run(() =>
        {
            // The spaces hang past the control, where the caret cannot be drawn: it is held at the edge, and spaced so each step still shows. Only a
            // line whose own text ends within the spaces' room of the edge has none to give (the host's margin is what gives it): its caret does not
            // go back, and stays inside the control (every line of a justified paragraph is such a line).
            int walked = 0, crowded = 0;
            for (double width = 150; width <= 330; width += 2)
            {
                var field = Field(text, rtl, align, width, false);
                var pl = field.ParagraphLayoutFor(0);
                for (int i = 0; i < pl.Lines.Count - 1; i++)
                {
                    var li = pl.Lines[i];
                    int core = Core(pl, li), end = pl.LineEnd(i, out _);
                    double first = pl.CaretXBeforeClamp(li, new global::Avalonia.Media.CharacterHit(core));
                    double room = (end - core) * ParagraphLayout.MinCaretStep;
                    bool fits = rtl ? first - room >= 0 : first + room <= width - ParagraphLayout.CaretWidth;
                    double least = fits ? 1.4 : 0;
                    if (!fits) crowded++;
                    field.Editor.SetCaret(new TextPosition(0, core));
                    double x = field.CaretRect().X;
                    for (int o = core + 1; o <= end; o++)
                    {
                        field.Press(rtl ? Key.Left : Key.Right);
                        Assert.Equal(o, field.Editor.Caret.Offset);
                        double next = field.CaretRect().X;
                        Assert.True(rtl ? next <= x - least : next >= x + least, "width " + width + " line " + i + ": the caret did not visibly move across the space at " + (o - 1) + " (" + x + " to " + next + ")");
                        Assert.True(next >= -0.01 && next <= width - ParagraphLayout.CaretWidth + 0.01, "the caret is outside the control at " + next);
                        x = next;
                    }
                    for (int o = end - 1; o >= core; o--)
                    {
                        field.Press(rtl ? Key.Right : Key.Left);
                        Assert.Equal(o, field.Editor.Caret.Offset);
                        double next = field.CaretRect().X;
                        Assert.True(rtl ? next >= x + least : next <= x - least, "width " + width + " line " + i + ": going back, the caret did not visibly move across the space at " + o + " (" + x + " to " + next + ")");
                        x = next;
                    }
                    walked++;
                }
            }
            Assert.True(walked > 100);
            if (align != 5) Assert.True(crowded * 5 < walked, "lines too crowded for the spaces' carets to show: " + crowded + " of " + walked);   // a justified line always ends at the edge
        });
        [Theory]
        [MemberData(nameof(Cases))]
        public void The_end_of_a_wrapped_line_is_never_drawn_outside_the_control(string text, bool rtl, int align) => Headless.Run(() =>
        {
            // No room beside the text area: the spaces hang past the control, and the caret stops at its edge so it stays seen.
            for (double width = 280; width <= 340; width += 3)
            {
                var field = Field(text, rtl, align, width, false);
                var pl = field.ParagraphLayoutFor(0);
                for (int i = 0; i < pl.Lines.Count - 1; i++)
                {
                    int end = pl.LineEnd(i, out var hit);
                    var caret = pl.CaretRect(end, false, hit);
                    Assert.True(caret.X >= -0.01 && caret.Right <= width + 0.01, "width " + width + " line " + i + ": the End caret is at " + caret.X + ", the control is " + width + " wide");
                }
            }
        });

        [Theory]
        [MemberData(nameof(Cases))]
        public void A_click_at_the_end_of_a_wrapped_line_stays_on_that_line_and_the_end_caret_is_where_it_would_click(string text, bool rtl, int align) => Headless.Run(() =>
        {
            var pl = Layout(text, rtl, align, 320);
            for (int i = 0; i < pl.Lines.Count - 1; i++)
            {
                var li = pl.Lines[i];
                int end = pl.LineEnd(i, out var hit);
                var caret = pl.CaretRect(end, false, hit);
                var clicked = pl.HitTestDetailed(new Point(caret.X, li.Y + 2));
                Assert.True(clicked.Offset == end || clicked.Offset == end - 1, "the click at the End caret gave " + clicked.Offset + ", the end is " + end);
                // A caret held at the edge of the control is inside the space it follows, so a click there is not at the end of the line.
                if (caret.X < pl.LayoutWidth - ParagraphLayout.CaretWidth - 0.01)
                    Assert.True(System.Math.Abs(pl.CaretRect(clicked.Offset, false, clicked.CaretHit).X - caret.X) < 1, "line " + i + ": the click at the End caret " + caret.X + " put the caret at " + pl.CaretRect(clicked.Offset, false, clicked.CaretHit).X);
            }
        });

        [Theory]
        [MemberData(nameof(Cases))]
        public void A_click_at_a_caret_stop_held_at_the_right_edge_of_the_control_gives_a_caret_at_that_stop(string text, bool rtl, int align) => Headless.Run(() =>
        {
            // The stop-by-x theories skip a stop within a caret's width of the right edge, where the control holds the caret inside it: this is its pin.
            // A line stretched to the edge, or text that ends there, has such a stop; the stops among the spaces it ends with are another matter.
            int pinned = 0;
            for (double width = 200; width <= 300; width += 1)
            {
                var pl = Layout(text, rtl, align, width);
                foreach (var li in pl.Lines)
                {
                    int core = pl.TextEnd(li);
                    while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
                    foreach (var stop in pl.VisualStops(li))
                    {
                        if (stop.Offset > core || stop.X <= width - ParagraphLayout.CaretWidth - 0.01) continue;
                        var hit = pl.HitTestDetailed(new Point(stop.X, li.Y + 2));
                        var caret = pl.CaretRect(hit.Offset, false, hit.CaretHit);
                        Assert.True(System.Math.Abs(caret.X - stop.X) < 1, "width " + width + ": a click at " + stop.X + " (the stop of offset " + stop.Offset + ") gave offset " + hit.Offset + ", whose caret is at " + caret.X);
                        pinned++;
                    }
                }
            }
            Assert.True(pinned > 10, "stops pinned: " + pinned);
        });

        [Theory]
        [MemberData(nameof(Cases))]
        public void A_click_at_a_caret_among_the_hanging_spaces_that_the_control_holds_apart_gives_a_caret_at_that_stop(string text, bool rtl, int align) => Headless.Run(() =>
        {
            // The carets of the spaces a wrapped line ends with are drawn apart inside the control's edge when it leaves no room (MinCaretStep):
            // the mouse follows the carets as drawn, so a click at one gives it and not a neighbour a space's width away.
            int pinned = 0;
            for (double width = 150; width <= 330; width += 1)
            {
                var pl = Field(text, rtl, align, width, false).ParagraphLayoutFor(0);
                for (int i = 0; i < pl.Lines.Count - 1; i++)
                {
                    var li = pl.Lines[i];
                    int core = Core(pl, li);
                    foreach (var stop in pl.VisualStops(li))
                    {
                        double first = pl.CaretXBeforeClamp(li, new global::Avalonia.Media.CharacterHit(core));
                        if (stop.Offset <= core || (rtl ? stop.X > first + 0.5 : stop.X < first - 0.5)) continue;   // a caret held over the text before the spaces is on the text
                        var hit = pl.HitTestDetailed(new Point(stop.X, li.Y + 2));
                        var caret = pl.CaretRect(hit.Offset, false, hit.CaretHit);
                        Assert.True(System.Math.Abs(caret.X - stop.X) < 1, "width " + width + ": a click at " + stop.X + " (the stop of offset " + stop.Offset + ") gave offset " + hit.Offset + ", whose caret is at " + caret.X);
                        pinned++;
                    }
                }
            }
            if (align != 5) Assert.True(pinned > 100, "stops pinned: " + pinned);   // a justified line ends at the edge: its spaces share one place
        });

        [Fact]
        public void A_line_with_room_to_spare_keeps_the_end_caret_after_its_space() => Headless.Run(() =>
        {
            var pl = Layout("one two three four five six seven", false, 0, 120);
            Assert.True(pl.Lines.Count > 2);
            for (int i = 0; i < pl.Lines.Count - 1; i++)
            {
                var li = pl.Lines[i];
                int core = pl.TextEnd(li);
                while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
                double ink = pl.SelectionRects(li.Start, core).Max(r => r.Right);
                int end = pl.LineEnd(i, out var hit);
                var caret = pl.CaretRect(end, false, hit);
                if (li.Line.WidthIncludingTrailingWhitespace <= pl.ContentWidth)
                    Assert.True(caret.X > ink + 1, "line " + i + ": the caret after the space sits past the last letter when the space fits: " + caret.X + " vs " + ink);
            }
        });
    }
}