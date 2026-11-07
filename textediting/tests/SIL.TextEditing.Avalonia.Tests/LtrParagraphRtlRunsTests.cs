using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A line of several runs with a right-to-left run in it is read from the runs as they are drawn, whatever the direction of the
    /// paragraph: Avalonia's own answers for such a line are wrong on 11.3 and on 12, and on 12 some ranges make it throw.</summary>
    public class LtrParagraphRtlRunsTests
    {
        static readonly string[] Words = { "fox", "طويلة", "12" };

        /// <summary>Texts of one to four words, each split into two runs (the second bold) at every position.</summary>
        static IEnumerable<(string Text, int Split)> Splits()
        {
            var texts = new List<string>();
            var layer = new List<string> { "" };
            for (int n = 1; n <= 4; n++)
            {
                layer = layer.SelectMany(t => Words.Select(w => t.Length == 0 ? w : t + " " + w)).ToList();
                texts.AddRange(layer);
            }
            foreach (var text in texts)
                for (int split = 1; split < text.Length; split++)
                    yield return (text, split);
        }

        static Paragraph TwoRuns(string text, int split, bool rtl = false)
        {
            var p = Para(rtl ? "ar" : "en", "en", text.Substring(0, split), "en", text.Substring(split));
            p.Rtl = rtl;
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Bold, 1);
            return p;
        }

        /// <summary>Each character of each run of the line with the box its run draws it in (x in paragraph coordinates).</summary>
        static List<(int Offset, double Left, double Right, bool Rtl)> DrawnBoxes(LineInfo li)
        {
            var boxes = new List<(int, double, double, bool)>();
            foreach (var slot in ParagraphLayout.RunSlots(li.Line))
                for (int o = slot.Start; o < slot.Start + slot.Length; o++)
                {
                    double a = slot.Distance(o)!.Value, b = slot.Distance(o + 1)!.Value;
                    boxes.Add((o, li.X + slot.X + Math.Min(a, b), li.X + slot.X + Math.Max(a, b), slot.RightToLeft));
                }
            return boxes;
        }

        [Fact]
        public void The_mouse_finds_the_character_it_is_over_on_a_left_to_right_paragraph_with_right_to_left_runs() => Headless.Run(() =>
        {
            var wrong = new List<string>();
            foreach (var (text, split) in Splits())
            {
                var pl = Fixtures.Field(Doc(TwoRuns(text, split)), 600).ParagraphLayoutFor(0);
                foreach (var li in pl.Lines)
                    foreach (var box in DrawnBoxes(li).Where(b => b.Offset < pl.TextEnd(li) && b.Right - b.Left >= 3))
                    {
                        double w = box.Right - box.Left;
                        int atLeft = pl.HitTestDetailed(new Point(box.Left + w * 0.25, li.Y + 2)).Offset;
                        int atRight = pl.HitTestDetailed(new Point(box.Left + w * 0.75, li.Y + 2)).Offset;
                        if (atLeft != (box.Rtl ? box.Offset + 1 : box.Offset) || atRight != (box.Rtl ? box.Offset : box.Offset + 1))
                            wrong.Add("\"" + text + "\" split at " + split + ", offset " + box.Offset + ": left " + atLeft + ", right " + atRight);
                    }
            }
            Assert.True(wrong.Count == 0, wrong.Count + " wrong hits, the first: " + string.Join("; ", wrong.Take(3)));
        });

        [Fact]
        public void The_caret_is_where_the_run_draws_the_character_on_a_left_to_right_paragraph_with_right_to_left_runs() => Headless.Run(() =>
        {
            var wrong = new List<string>();
            foreach (var (text, split) in Splits())
            {
                var pl = Fixtures.Field(Doc(TwoRuns(text, split)), 600).ParagraphLayoutFor(0);
                foreach (var li in pl.Lines)
                    foreach (var box in DrawnBoxes(li))
                    {
                        double expected = box.Rtl ? box.Right : box.Left;
                        var caret = pl.CaretRect(box.Offset, true, new global::Avalonia.Media.CharacterHit(box.Offset));
                        if (Math.Abs(caret.X - expected) >= 1) wrong.Add("\"" + text + "\" split at " + split + ", offset " + box.Offset + ": " + caret.X + " for " + expected);
                    }
            }
            Assert.True(wrong.Count == 0, wrong.Count + " wrong carets, the first: " + string.Join("; ", wrong.Take(3)));
        });

        [Fact]
        public void The_caret_stops_of_a_justified_left_to_right_line_with_right_to_left_runs_stay_inside_the_text_area_up_to_its_trailing_spaces() => Headless.Run(() =>
        {
            // The spaces a wrapped line ends with hang past its text; Avalonia 11.3 gives such a space the direction of the right-to-left word
            // before it, which would put the caret before it where the caret after it belongs.
            var texts = Words.SelectMany(a => Words.SelectMany(b => Words.SelectMany(c => Words.SelectMany(d => Words.Select(e => a + " " + b + " " + c + " " + d + " " + e))))).ToList();
            var wrong = new List<string>();
            int lines = 0;
            foreach (double width in new[] { 120.0, 160 })
                foreach (var text in texts)
                    for (int split = 1; split < text.Length; split += 3)
                    {
                        var p = TwoRuns(text, split);
                        p.Props.SetInt(TextPropertyNumbers.Align, 5);
                        p.Props.SetInt(TextPropertyNumbers.LeadingIndent, 40000, 1);
                        p.Props.SetInt(TextPropertyNumbers.TrailingIndent, 40000, 1);
                        var pl = Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
                        for (int i = 0; i < pl.Lines.Count - 1; i++)
                        {
                            var li = pl.Lines[i];
                            int core = pl.TextEnd(li);
                            while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
                            lines++;
                            foreach (var stop in pl.VisualStops(li).Where(st => st.Offset <= core))
                                if (stop.X < pl.ContentLeft - 0.5 || stop.X > pl.ContentLeft + pl.ContentWidth + 0.5)
                                    wrong.Add("\"" + text + "\" at " + width + " split at " + split + ", offset " + stop.Offset + " is at " + stop.X);
                        }
                    }
            Assert.True(lines > 1000);
            Assert.True(wrong.Count == 0, wrong.Count + " stops outside the text area, the first: " + string.Join("; ", wrong.Take(3)));
        });

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        public void A_left_to_right_line_with_right_to_left_words_and_three_spaces_can_be_selected_justified_and_drawn(int align) => Headless.Run(() =>
        {
            // Avalonia 12's TextLine.GetTextBounds throws for such a line; the line is read from its runs instead.
            var p = Para("en", "en", "dog عربية   كلمات");
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            p.Runs[0].Props.SetInt(TextPropertyNumbers.BackColor, 0xCCFFFF);
            var field = Fixtures.Field(Doc(p), 100);
            var pl = field.ParagraphLayoutFor(0);
            Assert.NotEmpty(pl.SelectionRects(0, p.Length));
            Assert.NotEmpty(pl.SelectionRects(2, 12));
            Fixtures.Render(field);
        });

#if NET10_0_OR_GREATER
        [Fact]
        public void Avalonia_12_throws_for_text_bounds_of_a_left_to_right_line_with_right_to_left_words_and_three_spaces() => Headless.Run(() =>
        {
            // Known fault of Avalonia 12. When this fails Avalonia has fixed it, and a left-to-right paragraph's line need not be read from its runs.
            var pl = Fixtures.Field(Doc(Para("en", "en", "dog عربية   كلمات")), 100).ParagraphLayoutFor(0);
            Assert.ThrowsAny<Exception>(() => pl.Lines.SelectMany(li => li.Line.GetTextBounds(0, li.Length)).ToList());
        });
#endif
    }
}
