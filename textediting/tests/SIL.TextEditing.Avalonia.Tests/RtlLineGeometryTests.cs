using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>The caret, the mouse and the selection on a right-to-left paragraph's line that is made of several runs. Avalonia's own
    /// answers (<c>TextLine.GetDistanceFromCharacterHit</c> and the others) put the caret of a left-to-right run inside the wrong run on such
    /// a line, on 11.3 and on 12, so the layout reads the geometry from the runs as they are drawn. The expected places here come from the
    /// drawn runs too: each character's box is where its run draws it.</summary>
    public class RtlLineGeometryTests
    {
        static Paragraph Latin()
        {
            var p = Para("ar", "en", "The quick brown ", "en", "fox jumps over the lazy dog while the five boxing", "en", " wizards jump quickly and then a mad boxer shot a quick gloved jab to the jaw of his dizzy opponent");
            p.Rtl = true;
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Bold, 1);
            return p;
        }

        static Paragraph Mixed()
        {
            var p = Para("ar", "en", "Some English words and ", "ar", "كلمات عربية طويلة بما يكفي لتلتف على عدة أسطر", "en", " then more English at the end of the paragraph so it wraps again and again and again");
            p.Rtl = true;
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Bold, 1);
            return p;
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (var width in new[] { 260.0, 300, 340 })
            {
                yield return new object[] { "latin", width };
                yield return new object[] { "mixed", width };
            }
        }

        static ParagraphLayout Layout(string which, double width) => Fixtures.Field(Doc(which == "latin" ? Latin() : Mixed()), width).ParagraphLayoutFor(0);

        /// <summary>Each character of each run of the line with the box its run draws it in (x in paragraph coordinates), left to right.</summary>
        static List<(int Offset, double Left, double Right, bool Rtl)> DrawnBoxes(LineInfo li)
        {
            var boxes = new List<(int, double, double, bool)>();
            foreach (var slot in ParagraphLayout.RunSlots(li.Line))
                for (int o = slot.Start; o < slot.Start + slot.Length; o++)
                {
                    double a = slot.Distance(o)!.Value, b = slot.Distance(o + 1)!.Value;
                    boxes.Add((o, li.X + slot.X + System.Math.Min(a, b), li.X + slot.X + System.Math.Max(a, b), slot.RightToLeft));
                }
            return boxes;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void The_caret_is_where_the_run_draws_the_character(string which, double width) => Headless.Run(() =>
        {
            var pl = Layout(which, width);
            Assert.True(pl.Lines.Count >= 3);
            foreach (var li in pl.Lines)
                foreach (var box in DrawnBoxes(li))
                {
                    // The leading edge of a character: its left in a left-to-right run, its right in a right-to-left one.
                    // (a caret at the edge of the control is held inside it)
                    double expected = System.Math.Min(box.Rtl ? box.Right : box.Left, width - ParagraphLayout.CaretWidth);
                    var caret = pl.CaretRect(box.Offset, true, new global::Avalonia.Media.CharacterHit(box.Offset));
                    Assert.True(System.Math.Abs(caret.X - expected) < 1, "line " + li.Start + ", offset " + box.Offset + ": the caret is at " + caret.X + ", the character is drawn from " + expected);
                }
        });

        [Theory]
        [MemberData(nameof(Cases))]
        public void The_mouse_finds_the_character_it_is_over(string which, double width) => Headless.Run(() =>
        {
            var pl = Layout(which, width);
            foreach (var li in pl.Lines)
                foreach (var box in DrawnBoxes(li).Where(b => b.Offset < pl.TextEnd(li)))
                {
                    if (box.Right - box.Left < 3) continue;
                    // A space the line ends with, whose caret the control holds inside its edge, is where that caret is drawn (LineEndCaretTests).
                    int core = pl.TextEnd(li);
                    while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
                    if (box.Offset >= core && System.Math.Abs(pl.CaretRect(pl.TextEnd(li), true).X - pl.CaretXBeforeClamp(li, new global::Avalonia.Media.CharacterHit(pl.TextEnd(li) - 1, 1))) > 0.01) continue;
                    double w = box.Right - box.Left;
                    int atLeft = pl.HitTestDetailed(new Point(box.Left + w * 0.25, li.Y + 2)).Offset;
                    int atRight = pl.HitTestDetailed(new Point(box.Left + w * 0.75, li.Y + 2)).Offset;
                    Assert.True(atLeft == (box.Rtl ? box.Offset + 1 : box.Offset), "line " + li.Start + ", offset " + box.Offset + ": the left of the character gave " + atLeft);
                    Assert.True(atRight == (box.Rtl ? box.Offset : box.Offset + 1), "line " + li.Start + ", offset " + box.Offset + ": the right of the character gave " + atRight);
                }
        });

        [Theory]
        [MemberData(nameof(Cases))]
        public void A_selection_covers_the_drawn_boxes_of_its_characters(string which, double width) => Headless.Run(() =>
        {
            var pl = Layout(which, width);
            foreach (var li in pl.Lines)
            {
                int end = pl.TextEnd(li);
                while (end > li.Start && pl.EffectiveText[end - 1] == ' ') end--;
                // A range from the middle of the line to its end, and the whole of it.
                foreach (int from in new[] { li.Start, (li.Start + end) / 2 })
                {
                    var boxes = DrawnBoxes(li).Where(b => b.Offset >= from && b.Offset < end).ToList();
                    var rects = pl.SelectionRects(from, end);
                    double expected = boxes.Sum(b => b.Right - b.Left), actual = rects.Sum(r => r.Width);
                    Assert.True(System.Math.Abs(expected - actual) < 1.5 + boxes.Count * 0.05, "line " + li.Start + ", from " + from + ": the selection is " + actual + " wide, the drawn characters " + expected);
                    Assert.True(rects.All(r => boxes.Any(b => r.X >= b.Left - 1.5 && r.Right <= b.Right + 1.5) || boxes.Where(b => b.Right > r.X - 1 && b.Left < r.Right + 1).Any()), "line " + li.Start + ": a selection box lies over no selected character");
                }
            }
        });

        [Fact]
        public void Avalonia_puts_the_caret_of_a_left_to_right_run_in_the_wrong_run_of_a_right_to_left_line_of_several_runs_on_both_lines() => Headless.Run(() =>
        {
            // Known fault of Avalonia 11.3 and 12: TextLine.GetDistanceFromCharacterHit puts offset 0 (the start of "The quick brown ", drawn at the
            // left of the line's text) inside the run after it. When this fails, Avalonia has fixed it: the layout need not read such lines
            // from their runs (LineInfo.UsesRuns) and this test, the layer and its tests go.
            var pl = Layout("latin", 300);
            var li = pl.Lines[0];
            Assert.True(li.UsesRuns);
            var first = DrawnBoxes(li).Single(b => b.Offset == 0);
            double drawnAt = first.Left;
            double avaloniaSays = li.X + li.Line.GetDistanceFromCharacterHit(new global::Avalonia.Media.CharacterHit(0));
            Assert.True(System.Math.Abs(avaloniaSays - drawnAt) > 20, "Avalonia says " + avaloniaSays + ", the character is drawn at " + drawnAt);
        });
    }
}
